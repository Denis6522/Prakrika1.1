using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
//
namespace BackupApp
{
    public class Config
    {
        public List<string> SourceDirectories { get; set; } = new List<string>();
        public string TargetDirectory { get; set; }
        public string LogLevel { get; set; } = "Info";
    }

    public class Logger
    {
        private readonly string _logFilePath;
        private readonly int _minLogLevel;

        public Logger(string logFilePath, string minLogLevel)
        {
            _logFilePath = logFilePath;
            _minLogLevel = minLogLevel.ToUpper() switch
            {
                "DEBUG" => 0,
                "INFO" => 1,
                "ERROR" => 2,
                _ => 1
            };
        }

        public void Log(string level, string message)
        {
            int levelValue = level.ToUpper() switch
            {
                "DEBUG" => 0,
                "INFO" => 1,
                "ERROR" => 2,
                _ => 1
            };

            if (levelValue < _minLogLevel) return;

            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
        }

        public void Debug(string message) => Log("DEBUG", message);
        public void Info(string message) => Log("INFO", message);
        public void Error(string message) => Log("ERROR", message);
    }

    class Program
    {
        static void Main(string[] args)
        {
            // Получаем путь к папке с исполняемым файлом
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string settingsDirectory = Path.Combine(exeDirectory, "Settings");

            // Создаем папку "Настройки", если её нет
            if (!Directory.Exists(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
            }

            string configPath = Path.Combine(settingsDirectory, "config.json");

            Console.WriteLine($"Поиск конфигурационного файла по пути: {configPath}");

            try
            {
                if (!File.Exists(configPath))
                {
                    CreateDefaultConfig(configPath);
                    Console.WriteLine("Создан конфигурационный файл по умолчанию. Пожалуйста, отредактируйте его и перезапустите программу.");
                    return;
                }

                Config config = LoadConfig(configPath);
                RunBackup(config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка: {ex.Message}");
            }

            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        private static void CreateDefaultConfig(string path)
        {
            var defaultConfig = new Config
            {
                SourceDirectories = new List<string>
                {
                    @"C:\temp",
                    @"C:\Windows\appcompat"
                },
                TargetDirectory = @"C:\Copy",
                LogLevel = "Debug"
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(defaultConfig, options);
            File.WriteAllText(path, json);
        }

        private static Config LoadConfig(string configPath)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException("Конфигурационный файл не найден", configPath);

            string json = File.ReadAllText(configPath);
            Config config = JsonSerializer.Deserialize<Config>(json);

            if (config.SourceDirectories == null || config.SourceDirectories.Count == 0)
                throw new ArgumentException("В конфигурации не указаны исходные директории");

            if (string.IsNullOrWhiteSpace(config.TargetDirectory))
                throw new ArgumentException("В конфигурации не указана целевая директория");

            // Проверка на дублирование путей
            var duplicatePaths = config.SourceDirectories
                .GroupBy(p => Path.GetFullPath(p.ToLower()))
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatePaths.Any())
            {
                throw new ArgumentException(
                    $"В конфигурации обнаружены дублирующиеся исходные пути: {string.Join(", ", duplicatePaths)}");
            }

            return config;
        }

        private static void RunBackup(Config config)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupDir = Path.Combine(config.TargetDirectory, timestamp);
            Directory.CreateDirectory(backupDir);

            string logPath = Path.Combine(backupDir, $"backup_{timestamp}.log");
            Logger logger = new Logger(logPath, config.LogLevel);

            logger.Info($"Резервное копирование начато. Целевая директория: {backupDir}");

            for (int i = 0; i < config.SourceDirectories.Count; i++)
            {
                string sourceDir = config.SourceDirectories[i];
                string sourceBackupDir = Path.Combine(backupDir, $"Source{i}");
                ProcessDirectory(sourceDir, sourceBackupDir, config, logger);
            }

            logger.Info("Резервное копирование завершено");
        }

        private static void ProcessDirectory(string sourceDir, string backupDir, Config config, Logger logger)
        {
            try
            {
                if (!Directory.Exists(sourceDir))
                {
                    logger.Error($"Исходная директория не найдена: {sourceDir}");
                    return;
                }

                logger.Info($"Обработка директории: {sourceDir}");

                Directory.CreateDirectory(backupDir);

                // Обработка файлов
                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    ProcessFile(file, backupDir, config, logger);
                }

                // Рекурсивная обработка поддиректорий
                foreach (string subDir in Directory.GetDirectories(sourceDir))
                {
                    string subDirName = Path.GetFileName(subDir);
                    string destSubDir = Path.Combine(backupDir, subDirName);
                    ProcessDirectory(subDir, destSubDir, config, logger);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
            {
                logger.Error($"Отказано в доступе к директории: {sourceDir}. Ошибка: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.Error($"Ошибка при обработке директории {sourceDir}: {ex.Message}");
            }
        }

        private static void ProcessFile(string sourceFile, string backupDir, Config config, Logger logger)
        {
            string fileName = Path.GetFileName(sourceFile);
            string destFile = Path.Combine(backupDir, fileName);

            try
            {
                File.Copy(sourceFile, destFile, overwrite: true);
                logger.Debug($"Скопирован: {fileName}");
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.Error($"Отказано в доступе к файлу: {sourceFile}. Ошибка: {ex.Message}");
            }
            catch (IOException ex)
            {
                logger.Error($"Ошибка ввода-вывода при копировании файла {sourceFile}: {ex.Message}");
            }
            catch (SecurityException ex)
            {
                logger.Error($"Ошибка безопасности при копировании файла {sourceFile}: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.Error($"Ошибка при копировании файла {sourceFile}: {ex.Message}");
            }
        }
    }
}