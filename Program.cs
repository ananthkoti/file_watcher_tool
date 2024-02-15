﻿using System;
using System.IO;
using System.Data.SqlClient;
using System.Timers;


namespace file_watcher_tool
{
    class Program
    {
         static readonly string connectionString = @"Data Source = (localdb)\MSSQLLocalDB ; Initial Catalog= FileWatcherDB; Integrated Security = True;";

         static readonly string lookupTableName = "LookUpTable";

         static readonly string transactionalTableName = "TransactionalTable";

          static void Main(string[] args)
        {
          

            StartFileMonitoring();

            StartHourlyReportGenerator();

            Console.WriteLine("File monitoring started. Press any key to exit.");
            Console.ReadKey();
        }

        static void StartFileMonitoring()
        {
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = @"C:\sample_file_watcher";
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Filter = "*.*";
            watcher.Created += OnFileCreated;
            watcher.Changed += OnFileChanged;
            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;
            watcher.EnableRaisingEvents = true;
        }
        static void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"A new file {e.Name} was created at {e.FullPath}");

            string FileName = e.Name;
            string FilePath = e.FullPath;
            DateTime BatchDate = DateTime.Now;
            DateTime ActualTime = DateTime.Now;
            long ActualSize = new FileInfo(FilePath).Length;
            DateTime EarliestExpectedTime = DetermineEarliestExpectedTime();
            DateTime DeadlineTime = DetermineDeadlineTime();
            string Schedule = DetermineSchedule(FilePath);
            string Status = DetermineStatus(ActualTime, EarliestExpectedTime, DeadlineTime);

            InsertRecordIntoLookUpTable(FileName, FilePath, EarliestExpectedTime, DeadlineTime, Schedule);
            InsertRecordIntoTransactionalTable(BatchDate, FileName, FilePath, ActualTime, ActualSize, Status);
        }

        static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"A new file {e.Name} was updated at {e.FullPath}");
            
            string FileName = e.Name;
            string FilePath = e.FullPath;
            DateTime BatchDate = DateTime.Now;
            DateTime ActualTime = DateTime.Now;
            long ActualSize = new FileInfo(FilePath).Length;
            
            

            UpdateRecordInTransactionalTable(FileName, FilePath, BatchDate, ActualTime, ActualSize);
        }

        static void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"A file {e.Name} was deleted from {e.FullPath}");
            string FileName = e.Name;
            string FilePath = e.FullPath;

            DeleteRecordFromTransactionalTable(FileName, FilePath);
        }

        static void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            string OldFileName = e.OldName;
            string OldFilePath = e.OldFullPath;
            string NewFileName = e.Name;
            string NewFilePath = e.FullPath;
            DateTime BatchDate = DateTime.Now;
            DateTime ActualTime = DateTime.Now;

            RenameRecordInTransactionalTable(NewFileName, NewFilePath, BatchDate, ActualTime, OldFileName, OldFilePath);

        }

        static void StartHourlyReportGenerator()
        {
            Timer hourlyTimer = new Timer();
            hourlyTimer.Interval = 60*60*1000;
            hourlyTimer.Elapsed += GenerateHourlyReport;
            hourlyTimer.Start();
        }

        static void GenerateHourlyReport(object sender, ElapsedEventArgs e)
        {
            DateTime BatchDate = DateTime.Now;
            string query = $"SELECT * FROM {transactionalTableName} WHERE BatchDate = '{BatchDate}' AND Status = 'overdue' ";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {

                using (SqlCommand command = new SqlCommand(query, connection))
                { 
                    connection.Open();
                    SqlDataReader reader = command.ExecuteReader();
                    Console.WriteLine($"Hourly report for {BatchDate}: ");
                    while (reader.Read())
                    {
                    Console.WriteLine($"File Name: {reader["FileName"]}, File Path: {reader["FilePath"]}");
                    }

                reader.Close();
                }

            }
        }

        static DateTime DetermineEarliestExpectedTime()
        {
            DateTime ActualTime = DateTime.Now;
            DateTime EarliestExpectedTime = DateTime.Today.AddHours(9).AddMinutes(00);
            if (ActualTime == EarliestExpectedTime)
            {
                return EarliestExpectedTime;
            }
            else
            {
                return DateTime.Today.AddDays(1).AddHours(9).AddMinutes(00);
            }
        }

        static DateTime DetermineDeadlineTime()
        {
            DateTime ActualTime = DateTime.Now;
            DateTime DeadlineTime = DateTime.Today.AddHours(19).AddMinutes(00);

            if (ActualTime >= DeadlineTime)
            {
                DeadlineTime = DateTime.Today.AddDays(1).AddHours(19).AddMinutes(00);
            }

            return DeadlineTime;
        }

        static string DetermineSchedule(string FilePath)
        {
            if (FilePath.Contains("weekly"))
            {
                return "Weekly Friday";
            }
            else if (FilePath.Contains("monthly"))
            {
                return "monthly Business day or Calender day";
            }
            else if (FilePath.Contains("yearly"))
            {
                return "yearly month Business day or Calender day";
            }
            else
            {
                return "daily Business day or calender day";
            }
        }

        static string DetermineStatus(DateTime ActualTime, DateTime EarliestExpectedTime, DateTime DeadlineTime)
        {
            if (ActualTime >= EarliestExpectedTime && ActualTime <= DeadlineTime)
            {
                return "due";
            }
            else if (ActualTime > DeadlineTime)
            {
                return "overdue";
            }
            else
            {
                return "published";
            }
        }

        static void InsertRecordIntoLookUpTable(string FileName, string FilePath, DateTime EarliestExpectedTime, DateTime DeadlineTime, string Schedule)
        {
            string query = $"INSERT INTO {lookupTableName} (FileName, FilePath, EarliestExpectedTime, DeadlineTime, Schedule) VALUES('{FileName}', '{FilePath}', '{EarliestExpectedTime}', '{DeadlineTime}', '{Schedule}')";
           
            using (SqlConnection connection = new SqlConnection(connectionString))
            {

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    int v = command.ExecuteNonQuery();
                    connection.Close();
                }
            }
        }

        static void InsertRecordIntoTransactionalTable(DateTime BatchDate, string FileName, string FilePath, DateTime ActualTime, long ActualSize, string Status)
        {
            string query = $"INSERT INTO {transactionalTableName} (BatchDate, FileName, FilePath, ActualTime, ActualSize, Status) VALUES ( '{BatchDate}', '{FileName}', '{FilePath}', '{ActualTime}', '{ActualSize}', '{Status}')";
            
            using (SqlConnection connection = new SqlConnection(connectionString))
            {

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    int v = command.ExecuteNonQuery();
                    connection.Close();
                }   
            }
        }

        static void UpdateRecordInTransactionalTable(string FileName, string FilePath, DateTime BatchDate, DateTime ActualTime, long ActualSize)
        {
            string query = $"UPDATE {transactionalTableName} SET  FilePath = '{FilePath}', ActualTime = '{ActualTime}', ActualSize = '{ActualSize}' WHERE BatchDate = '{BatchDate}' AND FileName = '{FileName}' ";
            

            using (SqlConnection connection = new SqlConnection(connectionString))
            {

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    int v =command.ExecuteNonQuery();
                    connection.Close();
                }
            }
        }

        static void DeleteRecordFromTransactionalTable(string FileName, string FilePath)
        {
            string query = $"DELETE FROM {transactionalTableName} WHERE FileName = '{FileName}' AND FilePath = '{FilePath}' ";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    int v = command.ExecuteNonQuery();
                    connection.Close();
                }
            }
        }

        static void RenameRecordInTransactionalTable(string NewFileName, string NewFilePath, DateTime BatchDate, DateTime ActualTime, string OldFileName, string OldFilePath)
        {
            string query = $"UPDATE {transactionalTableName} SET FileName = '{NewFileName}', FilePath = '{NewFilePath}', BatchDate = '{BatchDate}', ActualTime = '{ActualTime}' WHERE FileName = '{OldFileName}' AND FilePath = '{OldFilePath}' ";
           
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    int v = command.ExecuteNonQuery();
                    connection.Close();
                }
            }
        }
    }
}
