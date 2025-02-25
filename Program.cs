using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using DBSqlUpdate;

namespace UpdateCommandLine
{
    /// <summary>
    /// Piotr Kuliński (c) 2024
    /// Główny program konsolowy
    /// </summary>
    class Program
    {
        [DllImport("user32.dll")]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)] //string wParam
        public static extern string SendMessage(IntPtr hWnd, int msg, IntPtr lParam, IntPtr lParam1);

        [DllImport("user32.dll", CharSet = CharSet.Auto)] //string wParam
        public static extern string PostMessage(IntPtr hWnd, int msg, IntPtr lParam, IntPtr lParam1);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern uint RegisterWindowMessage(string lpString);

        private static FileStream _Progress = new FileStream("GKUpdate.progress", FileMode.Create, FileAccess.Write, FileShare.ReadWrite);


        /// <summary>
        /// Konfiguracja
        /// </summary>
        public static Configuration config = null;

        /// <summary>
        /// BusinessLogic, połączenie
        /// </summary>
        public static DBLogic connection = null;

        /// <summary>
        /// Model wzorcowy bazy danych
        /// </summary>
        public static DBModel pattern = null;

        /// <summary>
        /// Punkt startowy aplikacji, uzyskanie pomocy /?
        /// </summary>
        /// <param name="args"></param>
        static int Main(string[] args)
        {
            FileStream _ExitCode = new FileStream("DBSqlUpdate.exitcode", FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            FileStream _lock = new FileStream("DBSqlUpdate.lock", FileMode.Create, FileAccess.Write, FileShare.Write);

            try
            {
                _ExitCode.WriteByte(1); // rozpoczecie konwersji
                _ExitCode.Position = 0;
                try
                {
                    Console.BufferHeight =
                    Console.BufferWidth = 9000;
                    
                    Console.WindowHeight = (Console.LargestWindowHeight <= 50 ? Console.LargestWindowHeight : 50);
                    Console.WindowWidth = (Console.LargestWindowWidth <= 150 ? Console.LargestWindowWidth : 150);
                    Console.SetWindowSize(Console.WindowWidth, Console.WindowHeight);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                Console.CancelKeyPress += Console_CancelKeyPress;

                Helpers.OnError += Helpers.LoggerException;

                config = new Configuration();

                ParametersCommandLine.Prepare(args, ref config);

                if (config.Help)
                {
                    ParametersCommandLine.Usage(config);
                    _ExitCode.WriteByte(0);
                    return 0;
                }

                if (config.IsConsole)
                {
                    Helpers.OnMessage += MessageToConsole;
                    Helpers.OnError += ErrorLoggerConsole;
                }
                if (config.WMHandle != null)
                    Helpers.OnMessage += MessageToHWND;
                Helpers.OnMessage += Helpers.Logger;
                Helpers.OnSqlCommand += Helpers.Logger;

                connection = new DBLogic(config);

                try
                {
                    connection.ControlDatabase(config);
                }
                catch (Exception ex)
                {
                    if (ex.Data.Contains("InternalError"))
                        if ((GKUErrorNumber)ex.Data["InternalError"] == GKUErrorNumber.DataBaseNotExists)
                            connection.CreateDatabase(config.ConnectionString);
                }

                if (!connection.DbConnect())
                {
                    _ExitCode.WriteByte(1);
                    return 1;
                }
                // czy operacje na bazie danych, wówczas trzeba sprawdzić procedurę skryptującą
                if (config.ControlXMLProcedure)
                {
                    if (File.Exists(config.Template.FileIn))
                    {
                        Helpers.RegisterMessageFormat("Kontrola procedury skryptującej");
                        HelperDBStructureXML Struct = new HelperDBStructureXML(connection);
                        Struct.GetDBModel(ref pattern, new FileInfo(config.Template.FileIn));
                        Struct.CheckProcedureVersion(ref pattern);
                    }
                    else
                        GKUException.RegisterError("Wymuszono kontrolę procedury skryptującej");
                }

                if (config.IsBackup || config.IsRestore || config.IsList)
                {
                    CMDBackupRestore br = new CMDBackupRestore();
                    if (config.IsRestore && br.Restore() != EnumState.IsComplet)
                    {
                        _ExitCode.WriteByte(2);
                        return 2;
                    }

                    if (config.IsBackup && br.Backup() != EnumState.IsComplet)
                    {
                        _ExitCode.WriteByte(2);
                        return 2;
                    }

                    if (config.IsList)
                    {
                        br.List();
                    }
                }

                if (config.DownLoadFile)
                {
                    Helpers.OnSqlCommand -= Helpers.Logger;
                    config.OnProcessingFile += connection.DownloadFileFromDB;
                    config.ProcessingListFile(config.FileProfil);
                    Helpers.OnSqlCommand += Helpers.Logger;
                }

                if (config.Scripting)
                {
                    CMDScripting scripting = new CMDScripting();
                    scripting.Excecute(config.Template.FileOut);
                }

                CMDConvertion convert = null;				
                if (config.Convert && pattern != null)
                {
                    convert = new CMDConvertion();
                    convert.OnProgress += Convert_OnProgress;
                    convert.Excecute(); 
                    convert.OnProgress -= Convert_OnProgress;
                }

                if (Program.config.Shrink)
                {
                    Program.connection.Shrink();
                };

                if (config.SendFile)
                {
                    Helpers.OnSqlCommand -= Helpers.Logger;
                    config.OnProcessingFile += connection.SendFileToDB;
                    config.ProcessingListFile(config.FileProfil);
                    Helpers.OnSqlCommand += Helpers.Logger;
                }

                if (!Program.config.RunScript.Equals(""))
                {
                    if (convert == null)
                        convert = new CMDConvertion();
                    convert.ExecuteScript();
                }

                if (config.CreateUser)
                {
#if (DEBUG)
#else
                    Helpers.OnSqlCommand -= Helpers.Logger; // dla bezpieczeństwa nie zrzucam tych zdarzeń do loga
#endif
                    config.OnProcessingUser += connection.CreateAccess;
                    config.ProcessingListUser();
                }
                _ExitCode.WriteByte(0);
            }
            catch (Exception oe)
            {
                Console.WriteLine(oe.ToString());
                Helpers.RegisterError(oe);
                _ExitCode.WriteByte(3);
                return 3;
            }
            finally
            {
                if (_Progress != null && File.Exists(_Progress.Name))
                {
                    _Progress.Close();
                }

                if (_ExitCode != null && File.Exists(_ExitCode.Name))
                {
                    _ExitCode.Close();
                }

                if (_lock != null && File.Exists(_lock.Name))
                {
                    _lock.Close();
                    File.Delete(_lock.Name);
                }

                if (connection != null)
                {
                    if (connection.DbServer != null)
                        connection.DbServer.Close();

                    connection.DbServer.Dispose();
                    connection = null;
                }
            }
            Console.Beep();

            return 0;
        }

        private static void Convert_OnProgress(EventConvertProgressArgs e)
        {
            _Progress.Seek(0, SeekOrigin.Begin);
            _Progress.WriteByte((byte)e.ProgressValue);
            _Progress.Flush();
            //throw new NotImplementedException();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Przerwanie działania programu... ");

            ConsoleKeyInfo key = Console.ReadKey();
            if (key.Key.ToString().ToUpper() == "T")
                return;
            //throw new NotImplementedException();
        }

        /// <summary>
        /// Przechwycenie komunikatów z BL
        /// </summary>
        /// <param name="message"></param>
        public static void MessageToConsole(String message)
        {
            ConsoleColor CurrentColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ForegroundColor = CurrentColor;
        }

        /// <summary>
        /// Przechwycenie błędów z BL
        /// </summary>
        /// <param name="oe"></param>
        public static void ErrorLoggerConsole(Exception oe)
        {
            ConsoleColor CurrentColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            //Console.Beep();
            Console.WriteLine(oe.Message);
            Console.ForegroundColor = CurrentColor;
        }

        /// <summary>
        /// Wysłanie komunikatów do wskazanego okna
        /// </summary>
        /// <param name="message"></param>
        public static void MessageToHWND(String message)
        {
            const int WM_SETTEXT = 0X000C;
            IntPtr text = Marshal.StringToCoTaskMemUni(message);
            try
            {
                SendMessage(config.WMHandle, WM_SETTEXT, IntPtr.Zero, text);
            }
            catch (Exception)
            {
                ;
            }
            Marshal.FreeCoTaskMem(text);
            return;


            //Process notepadProccess = Process.GetProcessesByName("notepad")[0];

            //IntPtr notepadTextbox = FindWindowEx(notepadProccess.MainWindowHandle, IntPtr.Zero, "Edit", null);
            //try
            //{
            //    SendMessage(notepadTextbox, WM_SETTEXT, IntPtr.Zero, text);
            //}
            //catch (Exception oe) {; }
            //Marshal.FreeCoTaskMem(text);
        }
    }
}
