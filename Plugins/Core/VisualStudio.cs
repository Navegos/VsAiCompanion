﻿using JocysCom.VS.AiCompanion.Plugins.Core.VsFunctions;
using System.Collections.Generic;
using System.Text.Json;

namespace JocysCom.VS.AiCompanion.Plugins.Core
{
	/// <summary>
	/// Use to get selection or document content from Visual Studio.
	/// </summary>
	public partial class VisualStudio : ISolutionHelper
	{
		/// <summary>
		/// Current Visual Solution helper.
		/// </summary>
		public static ISolutionHelper Current;

		/// <inheritdoc />
		public DocItem GetSolution(bool includeContents)
			=> Current.GetSolution(includeContents);

		/// <inheritdoc />
		public IList<DocItem> GetSolutionProjects(string fileFullName, bool includeContents)
			=> Current.GetSolutionProjects(fileFullName, includeContents);

		/// <inheritdoc />
		public bool EditFormatDocument()
			=> Current.EditFormatDocument();

		/// <inheritdoc />
		public bool EditFormatSelection()
			=> Current.EditFormatSelection();

		/// <inheritdoc />
		public DocItem GetCurrentDocument(bool includeContents)
			=> Current.GetCurrentDocument(includeContents);

		/// <inheritdoc />
		public bool OpenDocument(string fileFullName)
			=> Current.OpenDocument(fileFullName);

		/// <inheritdoc />
		public bool SaveDocument(string fileFullName, string newFileName)
			=> Current.SaveDocument(fileFullName, newFileName);

		/// <inheritdoc />
		public bool CloseDocument(string fileFullName, bool save)
			=> Current.CloseDocument(fileFullName, save);

		/// <inheritdoc />
		public bool UndoDocument(string fileFullName)
			=> Current.UndoDocument(fileFullName);

		/// <inheritdoc />
		public IList<DocItem> GetAllSolutionDocuments(bool includeContents)
			=> Current.GetAllSolutionDocuments(includeContents);

		/// <inheritdoc />
		public ExceptionInfo GetCurrentException(bool includeDocItem, bool includeDocItemContents)
			=> Current.GetCurrentException(includeDocItem, includeDocItemContents);

		/// <inheritdoc />
		public IList<DocItem> GetDocumentsOfProjectOfCurrentDocument(bool includeContents)
			=> Current.GetDocumentsOfProjectOfCurrentDocument(includeContents);

		/// <inheritdoc />
		public IList<DocItem> GetDocumentsOfProjectOfSelectedDocument(bool includeContents)
			=> Current.GetDocumentsOfProjectOfSelectedDocument(includeContents);

		/// <inheritdoc />
		public IList<DocItem> GetDocumentsSelectedInExplorer(bool includeContents)
			=> Current.GetDocumentsSelectedInExplorer(includeContents);

		/// <inheritdoc />
		public IList<DocItem> GetOpenDocuments(bool includeContents)
			=> Current.GetOpenDocuments(includeContents);

		/// <inheritdoc />
		public IList<ErrorItem> GetErrors(
			ErrorLevel? errorLevel = null,
			string project = null,
			string fileFullName = null,
			bool includeDocItem = false,
			bool includeDocItemContents = false)
			=> Current.GetErrors(
				errorLevel,
				project,
				fileFullName,
				includeDocItem,
				includeDocItemContents
			);


		/// <inheritdoc />
		public IList<ErrorItem> GetSelectedErrors(bool includeDocItem, bool includeDocItemContents)
			=> Current.GetSelectedErrors(includeDocItem, includeDocItemContents);

		/// <inheritdoc />
		public DocItem GetSelection()
			=> Current.GetSelection();

		/// <inheritdoc />
		public bool SetCurrentDocumentContents(string contents)
			=> Current.SetCurrentDocumentContents(contents);

		/// <inheritdoc />
		public string ApplyCurrentDocumentContentsChanges(string unifiedDiff)
			=> Current.ApplyCurrentDocumentContentsChanges(unifiedDiff);

		/// <inheritdoc />
		public bool SetSelection(string contents)
			=> Current.SetSelection(contents);


		/// <inheritdoc />
		public string ApplySelectionChanges(string unifiedDiff)
			=> Current.ApplySelectionChanges(unifiedDiff);

		/// <inheritdoc />
		public Dictionary<string, JsonElement> GetEnvironmentContext()
			=> Current.GetEnvironmentContext();

		/// <inheritdoc />
		public string BuildSolutionProject(string fileFullName)
			=> Current.BuildSolutionProject(fileFullName);

		/// <inheritdoc />
		public string GetOutputContent(string type)
			=> Current.GetOutputContent(type);

	}
}
