﻿using JocysCom.ClassLibrary;
using JocysCom.VS.AiCompanion.Plugins.Core.UnifiedFormat;
using JocysCom.VS.AiCompanion.Plugins.Core.VsFunctions;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace JocysCom.VS.AiCompanion.Plugins.Core
{
	public partial class Basic
	{
		#region File Operations

		/// <summary>
		/// Read plain text content from files and documents. Supported document formats include: .docx, .xlsx, .xls, .pdf.
		/// Supports reading multiple files at once.
		/// </summary>
		/// <param name="paths">List of files to read from.</param>
		[RiskLevel(RiskLevel.Medium)]
		public List<OperationResult<string>> ReadFilesAsPlainText(string[] paths)
		{
			var list = new List<OperationResult<string>>();
			foreach (var path in paths)
			{
				var item = fileHelper.ReadFileAsPlainText(path);
				list.Add(item);
			}
			return list;
		}

		/// <summary>
		/// Read information and contents of files.
		/// </summary>
		/// <param name="paths">The list of files to read from.</param>
		[RiskLevel(RiskLevel.Medium)]
		public static List<DocItem> ReadFiles(string[] paths)
		{
			var list = new List<DocItem>();
			foreach (var path in paths)
			{
				var di = new DocItem(null, path);
				di.LoadData();
				list.Add(di);
			}
			return list;
		}

		/// <summary>
		/// Write file text content on user computer.
		/// </summary>
		/// <param name="path">The file to write to.</param>
		/// <param name="text">The string to write to the file.</param>
		/// <returns>True if the operation was successful.</returns>
		[RiskLevel(RiskLevel.High)]
		public static OperationResult<bool> WriteFileText(string path, string text)
		{
			try
			{
				var fi = new FileInfo(path);
				if (!fi.Directory.Exists)
					fi.Directory.Create();
				System.IO.File.WriteAllText(path, text);
				return new OperationResult<bool>(true);
			}
			catch (System.Exception ex)
			{
				return new OperationResult<bool>(ex);
			}
		}

		/// <summary>
		/// Write file byte content on user computer.
		/// </summary>
		/// <param name="path">The file to write to.</param>
		/// <param name="base64">The bytes represented as base64 to write to the file.</param>
		/// <returns>True if the operation was successful.</returns>
		[RiskLevel(RiskLevel.High)]
		public static OperationResult<bool> WriteFileBytes(string path, string base64)
		{
			try
			{
				var bytes = System.Convert.FromBase64String(base64);
				var fi = new FileInfo(path);
				if (!fi.Directory.Exists)
					fi.Directory.Create();
				System.IO.File.WriteAllBytes(path, bytes);
				return new OperationResult<bool>(true);
			}
			catch (System.Exception ex)
			{
				return new OperationResult<bool>(ex);
			}
		}

		#endregion

		#region Convert to PDF and Image


		/// <summary>
		/// Convert file to PDF.
		/// </summary>
		/// <param name="inputFilePath">Source file.</param>
		/// <param name="outputFilePath">Target PDF File.</param>
		/// <returns>True if the operation was successful.</returns>
		//[RiskLevel(RiskLevel.High)]
		public static OperationResult<bool> ConvertToPDF(string inputFilePath, string outputFilePath)
		{
			try
			{
				// Register the printer's output file path via the registry
				SetDefaultPrinterOutput(outputFilePath);

				// Create a new process to print the document
				ProcessStartInfo processInfo = new ProcessStartInfo()
				{
					Verb = "print",
					FileName = inputFilePath,
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden,
					// Specify the printer: Microsoft Print to PDF
					Arguments = "\"Microsoft Print to PDF\""
				};

				Process process = new Process()
				{
					StartInfo = processInfo
				};
				process.Start();
				process.WaitForExit();

				return new OperationResult<bool>(true);
			}
			catch (System.Exception ex)
			{
				return new OperationResult<bool>(ex);
			}
		}

		private static void SetDefaultPrinterOutput(string outputFilePath)
		{
			// Define the registry path for the printer
			string registryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Print\Printers\Microsoft Print to PDF\PrinterDriverData";

			// Try to open the registry key
			using (RegistryKey key = Registry.CurrentUser.CreateSubKey(registryPath, true))
			{
				if (key == null)
				{
					throw new System.Exception($"Registry path not found or inaccessible: {registryPath}");
				}

				// Set the default output file
				key.SetValue("OutputFile", outputFilePath, RegistryValueKind.String);
			}
		}

		#endregion

		#region IDiffHelper

		DiffHelper diffHelper = new DiffHelper();

		/// <inheritdoc/>
		public string CompareFilesAndReturnChanges(string originalFileFullName, string modifiedFileFullName)
			=> diffHelper.CompareFilesAndReturnChanges(originalFileFullName, modifiedFileFullName);

		/// <inheritdoc/>
		public string CompareContentsAndReturnChanges(string originalText, string modifiedText)
			=> diffHelper.CompareContentsAndReturnChanges(originalText, modifiedText);

		/// <inheritdoc/>
		public string ModifyFile(string fullFileName, string unifiedDiff)
			=> diffHelper.ModifyFile(fullFileName, unifiedDiff);


		/// <inheritdoc/>
		public string ModifyContents(string contents, string unifiedDiff)
			=> diffHelper.ModifyContents(contents, unifiedDiff);

		/*

		/// <inheritdoc/>
		public string PatchText(string contents, TextPatch[] textPatches)
			=> diffHelper.PatchText(contents, textPatches);

		*/

		#endregion

		#region IFileHelper

		FileHelper fileHelper = new FileHelper();

		/// <inheritdoc/>
		public string ModifyTextFile(string path, long startLine, long deleteLines, string insertContents = null)
			=> fileHelper.ModifyTextFile(path, startLine, deleteLines, insertContents);

		/// <inheritdoc/>
		public string ReadTextFile(string path, long offset = 0, long length = long.MaxValue)
			=> fileHelper.ReadTextFile(path, offset, length);

		/// <inheritdoc/>
		public string ReadTextFileLines(string path, long line = 1, long count = long.MaxValue)
			=> fileHelper.ReadTextFileLines(path, line, count);

		#endregion

	}
}
