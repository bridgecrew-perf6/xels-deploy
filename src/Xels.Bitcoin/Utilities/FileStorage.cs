﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Xels.Bitcoin.Utilities
{
    public class FileStorageOption
    {
        internal static FileStorageOption Default { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the output file content should be indented. Default value is false.
        /// </summary>
        /// <value>
        ///   <c>true</c> if output file content is indented; otherwise, <c>false</c>.
        /// </value>
        public bool Indent { get; set; }

        /// <summary>
        /// A value indicating whether to save a backup of the file. Default value is false.
        /// </summary>
        /// <value>
        ///   <c>true</c> to save a backup file; otherwise, <c>false</c>.
        /// </value>
        public bool SaveBackupFile { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a null value should be serialized in the output file. Default value is true.
        /// </summary>
        /// <value>
        ///   <c>true</c> if null values are serialized; otherwise, <c>false</c>.
        /// </value>
        public bool SerializeNullValues { get; set; }

        public FileStorageOption()
        {
            this.SaveBackupFile = false;
            this.Indent = false;
            this.SerializeNullValues = true;
        }

        static FileStorageOption()
        {
            Default = new FileStorageOption();
        }

        /// <summary>
        /// Gets the serialization settings based on current options.
        /// </summary>
        /// <returns></returns>
        internal JsonSerializerSettings GetSerializationSettings()
        {
            // get default Json serializer settings
            var settings = new JsonSerializerSettings
            {
                NullValueHandling = this.SerializeNullValues ? NullValueHandling.Include : NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };
            return settings;
        }
    }

    /// <summary>
    /// Class providing methods to save objects as files on the file system.
    /// </summary>
    /// <typeparam name="T">The type of object to be stored in the file system.</typeparam>
    public sealed class FileStorage<T> where T : new()
    {
        /// <summary> Gets the folder path. </summary>
        public string FolderPath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileStorage{T}"/> class.
        /// </summary>
        /// <param name="folderPath">The path of the folder in which the files are to be stored.</param>
        public FileStorage(string folderPath)
        {
            Guard.NotEmpty(folderPath, nameof(folderPath));

            this.FolderPath = folderPath;

            // Create a folder if none exists.
            Directory.CreateDirectory(folderPath);
        }

        /// <summary>
        /// Deletes a file from storage.
        /// </summary>
        /// <param name="fileName">The name of the file to delete.</param>
        public void DeleteFile(string fileName)
        {
            Guard.NotEmpty(fileName, nameof(fileName));

            string filePath = Path.Combine(this.FolderPath, fileName);
            File.Delete(filePath);
        }

        /// <summary>
        /// Saves an object to a file, optionally keeping a backup of it.
        /// </summary>
        /// <param name="toSave">Object to save as a file.</param>
        /// <param name="fileName">Name of the file to be saved.</param>
        /// <param name="options">The serialization options.</param>
        public void SaveToFile(T toSave, string fileName, FileStorageOption options = null)
        {
            Guard.NotEmpty(fileName, nameof(fileName));
            Guard.NotNull(toSave, nameof(toSave));

            if (options == null)
                options = FileStorageOption.Default;

            string filePath = Path.Combine(this.FolderPath, fileName);
            long uniqueId = DateTime.UtcNow.Ticks;
            string newFilePath = $"{filePath}.{uniqueId}.new";
            string tempFilePath = $"{filePath}.{uniqueId}.temp";

            File.WriteAllText(newFilePath, JsonConvert.SerializeObject(toSave, options.Indent ? Formatting.Indented : Formatting.None, options.GetSerializationSettings()));

            // If the file does not exist yet, create it.
            if (!File.Exists(filePath))
            {
                File.Move(newFilePath, filePath);

                if (options.SaveBackupFile)
                {
                    File.Copy(filePath, $"{filePath}.bak", true);
                }

                return;
            }

            if (options.SaveBackupFile)
            {
                File.Copy(filePath, $"{filePath}.bak", true);
            }

            // Delete the file and rename the temp file to that of the target file.
            File.Move(filePath, tempFilePath);
            File.Move(newFilePath, filePath);

            try
            {
                File.Delete(tempFilePath);
            }
            catch (IOException)
            {
                // Marking the file for deletion in the future.
                File.Move(tempFilePath, $"{ filePath}.{ uniqueId}.del");
            }
        }

        /// <summary>
        /// Checks whether a file with the specified name exists in the folder.
        /// </summary>
        /// <param name="fileName">The name of the file to look for.</param>
        /// <returns>A value indicating whether the file exists in the file system.</returns>
        public bool Exists(string fileName)
        {
            Guard.NotEmpty(fileName, nameof(fileName));

            string filePath = Path.Combine(this.FolderPath, fileName);
            return File.Exists(filePath);
        }

        /// <summary>
        /// Gets the paths of the files with the specified extension.
        /// </summary>
        /// <param name="fileExtension">The file extension.</param>
        /// <returns>A list of paths for files with the specified extension.</returns>
        public IEnumerable<string> GetFilesPaths(string fileExtension)
        {
            Guard.NotEmpty(fileExtension, nameof(fileExtension));
            return Directory.EnumerateFiles(this.FolderPath, $"*.{fileExtension}", SearchOption.TopDirectoryOnly);
        }

        /// <summary>
        /// Gets the names of files with the specified extension.
        /// </summary>
        /// <param name="fileExtension">The file extension.</param>
        /// <returns>A list of filenames with the specified extension.</returns>
        public IEnumerable<string> GetFilesNames(string fileExtension)
        {
            Guard.NotEmpty(fileExtension, nameof(fileExtension));

            IEnumerable<string> filesPaths = this.GetFilesPaths(fileExtension);
            return filesPaths.Select(p => Path.GetFileName(p));
        }

        /// <summary>
        /// Loads an object from the file in which it is persisted.
        /// </summary>
        /// <param name="fileName">The name of the file to load.</param>
        /// <returns>An object of type <see cref="T"/>.</returns>
        /// <exception cref="FileNotFoundException">Indicates that no file with this name was found.</exception>
        public T LoadByFileName(string fileName)
        {
            Guard.NotEmpty(fileName, nameof(fileName));

            string filePath = Path.Combine(this.FolderPath, fileName);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"No file found at {filePath}");

            return JsonConvert.DeserializeObject<T>(File.ReadAllText(filePath));
        }

        /// <summary>
        /// Loads all the objects that have file with the specified extension.
        /// </summary>
        /// <param name="fileExtension">The file extension.</param>
        /// <returns>A list of objects of type <see cref="T"/> whose persisted files have the specified extension. </returns>
        public IEnumerable<T> LoadByFileExtension(string fileExtension)
        {
            Guard.NotEmpty(fileExtension, nameof(fileExtension));

            // Get the paths of files with the extension
            IEnumerable<string> filesPaths = this.GetFilesPaths(fileExtension);

            var files = new List<T>();
            foreach (string filePath in filesPaths)
            {
                string fileName = Path.GetFileName(filePath);

                // Load the file into the object of type T.
                T loadedFile = this.LoadByFileName(fileName);
                files.Add(loadedFile);
            }

            return files;
        }
    }
}
