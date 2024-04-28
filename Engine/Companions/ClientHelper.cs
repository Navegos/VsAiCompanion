﻿using JocysCom.ClassLibrary.Configuration;
using JocysCom.VS.AiCompanion.Engine.Companions.ChatGPT;
using JocysCom.VS.AiCompanion.Engine.Controls.Chat;
using JocysCom.VS.AiCompanion.Plugins.Core;
using JocysCom.VS.AiCompanion.Plugins.Core.VsFunctions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JocysCom.VS.AiCompanion.Engine.Companions
{
	public static class ClientHelper
	{

		public const string PreviewModeMessage = "Preview Mode - Sending messages to AI is suppressed.";
		public const string UserName = "User";
		public const string SystemName = "System";
		public const string AiName = "Ai";


		public static string JoinMessageParts(params string[] args)
		{
			return string.Join("\r\n\r\n", args.Where(x => !string.IsNullOrEmpty(x)));
		}

		public static string ConvertAttachmentsToString(params MessageAttachments[] attachments)
		{
			var s = "";
			foreach (var a in attachments)
			{
				s += $"\r\n\r\n{a.Title}:";
				if (!string.IsNullOrEmpty(a.Instructions))
					s += $"\r\n\r\n{a.Instructions}";
				s += $"\r\n\r\n{a.Data}";
				s = s.Trim('\r', '\n');
			}
			return s;
		}

		public static List<chat_completion_message> ConvertMessageItemToChatMessage(bool isSystemInstructions, MessageItem message, bool? includeAttachments = null)
		{
			var completionMessages = new List<chat_completion_message>();
			var body = message.Body;
			if (message.Type == MessageType.In || message.Type == MessageType.Out)
			{
				var attachments = Array.Empty<MessageAttachments>();
				if (includeAttachments == true)
					attachments = message.Attachments.ToArray();
				if (includeAttachments == null)
					attachments = message.Attachments.Where(x => x.IsAlwaysIncluded).ToArray();
				if (attachments.Length > 0)
					body = JoinMessageParts(body, ConvertAttachmentsToString(attachments));
			}
			if (message.Type == MessageType.In)
			{
				// Add AI assitant message.
				completionMessages.Add(new chat_completion_message(message_role.assistant, body));
				return completionMessages;
			}
			if (message.Type == MessageType.Out)
			{

				// Add system message.
				if (isSystemInstructions && !string.IsNullOrEmpty(message.BodyInstructions))
					completionMessages.Add(new chat_completion_message(message_role.system, message.BodyInstructions));
				// Add user message.
				var userContent = isSystemInstructions
					? body
					: JoinMessageParts(message.BodyInstructions, body);
				completionMessages.Add(new chat_completion_message(message_role.user, userContent));
			}
			return completionMessages;
		}

		public async static Task Send(TemplateItem item,
			Action executeBeforeAddMessage = null,
			string overrideText = null,
			MessageItem overrideMessage = null
			)
		{
			System.Diagnostics.Debug.WriteLine($"Send on Item: {item.Name}");
			if (!Global.IsGoodSettings(item.AiService, true))
				return;
			if (!Global.ValidateServiceAndModel(item))
				return;
			if (item.IsBusy)
				return;
			if (string.IsNullOrEmpty(item.AiModel))
			{
				Global.SetWithTimeout(MessageBoxImage.Warning, "Please select an AI model from the dropdown.");
				return;
			}
			MessageItem m;
			string embeddingText = null;
			if (overrideMessage == null)
			{
				if (item.Messages == null)
					item.Messages = new BindingList<MessageItem>();
				// If task panel then allow to use AutoClear.
				var isTask = Global.Tasks.Items.Contains(item);
				// Message is added. Cleanup now.
				var itemText = overrideText ?? item.Text;
				if (isTask)
				{
					if (item.MessageBoxOperation == MessageBoxOperation.ClearMessage)
						item.Text = "";
					if (item.MessageBoxOperation == MessageBoxOperation.ResetMessage)
					{
						var template = Global.GetSettingItems(ItemType.Template)
							.Cast<TemplateItem>()
							.Where(x => x.Name == item.TemplateName)
							.FirstOrDefault();
						if (template != null)
							item.Text = template.Text;
					}
				}

				if (item.AutoFormatMessage)
					itemText = await FormatMessage(item, itemText);
				embeddingText = itemText;
				var vsData = AppHelper.GetMacroValues();
				// Prepare instructions.
				var instructions = item.TextInstructions;
				if (item.UseMacros)
					instructions = AppHelper.ReplaceMacros(instructions, vsData);
				m = new MessageItem(UserName, itemText, MessageType.Out);
				m.BodyInstructions = instructions;
				if (item.UseMacros)
					m.Body = AppHelper.ReplaceMacros(m.Body, vsData);
				var fileItems = new List<DocItem>();
				var at = item.AttachContext;
				// If data from context lists.
				var listNames = new List<string> {
					item.Context0ListName,
					item.Context1ListName,
					item.Context2ListName,
					item.Context3ListName,
					item.Context4ListName,
					item.Context5ListName,
				};
				// Get all enabled non-empty lists.
				var listInfos = Global.Lists.Items
					.Where(x => x.IsEnabled && x.Items?.Count > 0)
					.Where(x => listNames.Contains(x.Name))
					.ToList();
				for (int i = 0; i < listInfos.Count; i++)
				{
					var li = listInfos[i];
					var liForJson = new ListInfo()
					{
						Path = li.Path,
						Name = li.Name,
						Instructions = li.Instructions,
						IsReadOnly = li.IsReadOnly,
						Items = new BindingList<ListItem>(li.Items.ToList()),
					};
					var data = Client.Serialize(liForJson);
					liForJson.Items.Clear();
					var listAttachment = new MessageAttachments()
					{

						Title = li.Name,
						Instructions = li.Instructions,
						Type = ContextType.None,
						Data = data,
					};
					m.Attachments.Add(listAttachment);
				}
				// If data from clipboard.
				if (at.HasFlag(ContextType.Clipboard))
				{
					var clip = AppHelper.GetClipboard();
					var clipAttachment = new MessageAttachments()
					{
						Title = Global.AppSettings.ContextDataTitle,
						Type = item.AttachContext,
						Data = clip.ContentData,
					};
					m.Attachments.Add(clipAttachment);
				}
				if (Global.IsVsExtension)
				{
					// If text selection in Visual Studio.
					if (at.HasFlag(ContextType.Selection))
					{
						var ad = Global._SolutionHelper.GetSelection();
						var adAttachment = new MessageAttachments(ContextType.Selection, ad.Language, ad.ContentData);
						m.Attachments.Add(adAttachment);
					}
					// If selected error in Visual Studio.
					if (at.HasFlag(ContextType.Error))
					{
						var includeDocItemContents = at.HasFlag(ContextType.ErrorDocument);
						var errs = Global._SolutionHelper.GetSelectedErrors(true, includeDocItemContents);
						foreach (var err in errs)
						{
							if (string.IsNullOrEmpty(err?.Description))
								continue;
							var errorAttachment = new MessageAttachments(ContextType.Error, err);
							m.Attachments.Add(errorAttachment);
						}
					}
					// If active open document in Visual Studio.
					if (at.HasFlag(ContextType.ActiveDocument))
					{
						var ad = Global._SolutionHelper.GetCurrentDocument(true);
						var adAttachment = new MessageAttachments(ContextType.ActiveDocument, ad.Language, ad.ContentData);
						m.Attachments.Add(adAttachment);
					}
					if (at.HasFlag(ContextType.OpenDocuments))
						fileItems.AddRange(Global._SolutionHelper.GetOpenDocuments(true));
					if (at.HasFlag(ContextType.SelectedDocuments))
						fileItems.AddRange(Global._SolutionHelper.GetDocumentsSelectedInExplorer(true));
					if (at.HasFlag(ContextType.CurrentProject))
						fileItems.AddRange(Global._SolutionHelper.GetDocumentsOfProjectOfCurrentDocument(true));
					if (at.HasFlag(ContextType.SelectedProject))
						fileItems.AddRange(Global._SolutionHelper.GetDocumentsOfProjectOfSelectedDocument(true));
					if (at.HasFlag(ContextType.Solution))
						fileItems.AddRange(Global._SolutionHelper.GetAllSolutionDocuments(true));
					if (at.HasFlag(ContextType.Exception))
					{
						var includeDocItemContents = at.HasFlag(ContextType.ExceptionDocuments);
						var ei = Global._SolutionHelper.GetCurrentException(true, includeDocItemContents);
						if (!string.IsNullOrEmpty(ei?.Message))
						{
							var exceptionAttachment = new MessageAttachments(ContextType.Exception, ei);
							m.Attachments.Add(exceptionAttachment);
						}
					}
				}
				// Attach files as message attachments at the end.
				if (fileItems.Count > 0)
				{
					var a2 = new MessageAttachments()
					{
						Title = Global.AppSettings.ContextFileTitle,
						Type = item.AttachContext,
						Data = DocItem.ConvertFile(fileItems),
					};
					m.Attachments.Add(a2);
				}
				// Mark message as preview is preview.
				m.IsPreview = item.IsPreview;
				// ShowSensitiveDataWarning
				if (fileItems.Count > 0 && Global.AppSettings.ShowDocumentsAttachedWarning)
				{
					var lines = new List<string>();
					foreach (var fileItem in fileItems)
					{
						if (string.IsNullOrEmpty(fileItem.ContentData))
							continue;
						var word = AppHelper.ContainsSensitiveData(fileItem.ContentData);
						if (string.IsNullOrEmpty(word))
							continue;
						lines.Add($"Word '{word}' in File: {fileItem.FullName}\r\n");
					}
					if (lines.Count > 0)
					{
						var text = JoinMessageParts(
							"Possible sensitive data has been detected. Do you want to send these files to AI?",
							string.Join("\r\n", lines)
						);
						var caption = $"{Global.Info.Product} - Send Files";
						var result = MessageBox.Show(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
						if (result != MessageBoxResult.Yes)
							return;
					}
				}
				// ShowDocumentsAttachedWarning
				if (fileItems.Count > 0 && Global.AppSettings.ShowDocumentsAttachedWarning)
				{
					var files = fileItems.Select(x => x.FullName).ToList();
					var text = JoinMessageParts(
						"Do you want to send these files to AI?",
						string.Join("\r\n", files)
					);
					var caption = $"{Global.Info.Product} - Send Files";
					var result = MessageBox.Show(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
					if (result != MessageBoxResult.Yes)
						return;
				}
			}
			else
			{
				m = overrideMessage;
			}
			// Get current message with all attachments.
			var chatLogMessages = ConvertMessageItemToChatMessage(item.IsSystemInstructions, m, includeAttachments: true);
			// Prepare list of messages to send.
			if (item.AttachContext.HasFlag(ContextType.ChatHistory))
			{
				// Get tokens available.
				var tokensLeftForChatHistory = GetAvailableTokens(item, chatLogMessages, item.UseMaximumContext);
				var historyMessages = item.Messages
					// Exclude preview messages from the history.
					.Where(x => !x.IsPreview)
					.SelectMany(x => ConvertMessageItemToChatMessage(item.IsSystemInstructions, x, includeAttachments: null)).ToList();
				var attachMessages = AppHelper.GetMessages(historyMessages, tokensLeftForChatHistory, ChatLogOptions);
				chatLogMessages = attachMessages.Concat(chatLogMessages).ToList();
				if (Client.IsTextCompletionMode(item.AiModel) && attachMessages.Count > 0)
				{
					// Create attachment.
					var a0 = new MessageAttachments();
					a0.Title = Global.AppSettings.ContextChatTitle;
					a0.Instructions = Global.AppSettings.ContextChatInstructions;
					a0.Type = ContextType.ChatHistory;
					var options = new JsonSerializerOptions();
					options.WriteIndented = true;
					var json = JsonSerializer.Serialize(attachMessages, ChatLogOptions);
					a0.Data = $"```json\r\n{json}\r\n```";
					a0.IsMarkdown = true;
					// Update messages.
					var message = ConvertMessageItemToChatMessage(false, m, includeAttachments: true);
					var content = JoinMessageParts(message[0].content as string, ConvertAttachmentsToString(a0));
					chatLogMessages.Clear();
					chatLogMessages.Add(new chat_completion_message(message_role.user, content));
				}
			}
			var maxTokens = Client.GetMaxInputTokens(item);
			// Add the message item to the message list once all the content is added.
			// Adding the message will trigger an event that serializes and adds this message to the Chat HTML page.
			executeBeforeAddMessage?.Invoke();
			item.Messages.Add(m);
			item.Modified = DateTime.Now;
			var msgTokens = CountTokens(chatLogMessages, ChatLogOptions);
			if (item.IsPreview)
			{
				var message = new MessageItem(SystemName, Resources.Resources.Preview_Mode_Message);
				item.Messages.Add(message);
				item.Modified = DateTime.Now;
			}
			else if (maxTokens < msgTokens)
			{
				var message = new MessageItem(SystemName, $"Message is too big. Message Tokens: {msgTokens}, Maximum Tokens: {maxTokens}", MessageType.Error);
				item.Messages.Add(message);
				item.Modified = DateTime.Now;
			}
			else
			{
				try
				{
					if (item.AutoGenerateTitle)
					{
						item.AutoGenerateTitle = false;
						_ = GenerateTitle(item);
					}
					var client = new Companions.ChatGPT.Client(item.AiService);
					var maxInputTokens = Client.GetMaxInputTokens(item);
					// Send body and context data. Make sure it runs on NON-UI thread.
					var messageItems = await Task.Run(async () => await client.QueryAI(
						item.AiModel,
						chatLogMessages,
						item.Creativity,
						item,
						maxInputTokens,
						embeddingText
					)).ConfigureAwait(true);
					// If assistant message was received.
					var assistantMessage = messageItems.FirstOrDefault();
					if (assistantMessage != null)
					{
						// Workaround: Re-add 
						//if (item.Messages.Contains(assistantMessage) && assistantMessage.Attachments.Count > 1)
						//	item.Messages.Remove(assistantMessage);
						if (!item.Messages.Contains(assistantMessage))
						{
							item.Messages.Add(assistantMessage);
							item.Modified = DateTime.Now;
						}
						// Automation.
						SetData(item, assistantMessage.Body);
					}
					// If auto-reply message was added then...
					var userMessage = messageItems.Skip(1).FirstOrDefault();
					if (userMessage != null)
					{
						await Send(item, overrideMessage: userMessage);
					}
				}
				catch (Exception ex)
				{
					AddException(item, ex);
				}
			}
			// If item type task, then allow to do auto removal.
			if (Global.Tasks.Items.Contains(item) && item.AutoRemove)
				_ = Global.MainControl.Dispatcher.BeginInvoke(new Action(() => { _ = Global.Tasks.Items.Remove(item); }));
		}

		public static JsonSerializerOptions ChatLogOptions = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
			// Serialize enums as string for AI to understand.
			Converters = { new JsonStringEnumConverter() }
		};

		public static int GetAvailableTokens(TemplateItem item, List<chat_completion_message> messages = null, bool useMaximumContext = false)
		{
			var maxTokens = Client.GetMaxInputTokens(item);
			// Split 50%/50% between request and response.
			var maxRequesTokens = useMaximumContext
				? maxTokens
				: maxTokens / 2;
			var usedTokens = CountTokens(messages, ChatLogOptions);
			var availableTokens = maxRequesTokens - usedTokens;
			return availableTokens;
		}

		#region Reserved Tempalte Functions

		public async static Task<string> FormatMessage(TemplateItem item, string text)
		{
			if (string.IsNullOrEmpty((text ?? "").Trim()))
				return text;
			/// Try to get reserved template to generate title.
			var rItem = Global.Templates.Items.FirstOrDefault(x => x.Name == SettingsSourceManager.TemplateFormatMessageTaskName);
			if (rItem == null)
				return text;
			var messages = new List<chat_completion_message>();
			// Crate a copy in order not to add to existing list.
			try
			{
				// Add instructions to generate title to existing messages.
				messages.Add(new chat_completion_message(message_role.system, rItem.TextInstructions));
				// Supply data for processing.
				messages.Add(new chat_completion_message(message_role.user, text));
				var client = new Companions.ChatGPT.Client(item.AiService);
				var maxInputTokens = Client.GetMaxInputTokens(rItem);
				// Send body and context data. Make sure it runs on NON-UI thread.
				var messageItem = await Task.Run(async () => await client.QueryAI(
					rItem.AiModel,
					messages,
					rItem.Creativity,
					item,
					maxInputTokens,
					null
				)).ConfigureAwait(true);
				return messageItem.FirstOrDefault()?.Body ?? text;
			}
			catch (Exception ex)
			{
				AddException(item, ex);
			}
			return text;
		}

		public async static Task GenerateTitle(TemplateItem item)
		{
			/// Try to get reserved template to generate title.
			var rItem = Global.Templates.Items.FirstOrDefault(x => x.Name == SettingsSourceManager.TemplateGenerateTitleTaskName);
			if (rItem == null)
				return;
			if (item.Messages.Count == 0)
				return;
			var availableTokens = GetAvailableTokens(item, null);
			var allmessages = item.Messages
				// Exclude preview messages from the history.
				//.Where(x => !x.IsPreview)
				.SelectMany(x => ConvertMessageItemToChatMessage(item.IsSystemInstructions, x, false)).ToList();
			var messages = AppHelper.GetMessages(allmessages, availableTokens, ChatLogOptions);
			// Crate a copy in order not to add to existing list.
			try
			{
				// Add instructions to generate title to existing messages.
				messages.Add(new chat_completion_message(message_role.system, rItem.TextInstructions));
				var client = new Companions.ChatGPT.Client(item.AiService);
				var maxInputTokens = Client.GetMaxInputTokens(rItem);
				// Send body and context data. Make sure it runs on NON-UI thread.
				var response = await Task.Run(async () => await client.QueryAI(
					rItem.AiModel,
					messages,
					rItem.Creativity,
					item,
					maxInputTokens,
					null
				)).ConfigureAwait(true);
				var body = response.FirstOrDefault()?.Body;
				if (!string.IsNullOrEmpty(body))
				{
					body = SettingsData<object>.RemoveInvalidFileNameChars(body);
					if (body.Split().Length > 0)
					{
						var title = string.Join(" ", body.Split().Take(6).ToList());
						if (Global.Tasks.Items.Contains(item))
							Global.Tasks.RenameItem(item, title);
					}
				}
			}
			catch (Exception ex)
			{
				AddException(item, ex);
			}
		}

		public async static Task<string> EvaluateToolExecutionSafety(TemplateItem item, CancellationTokenSource cancellationToken)
		{
			/// Try to get reserved template to generate title.
			var rItem = Global.Templates.Items.FirstOrDefault(x => x.Name == item.PluginApprovalTemplate);
			if (rItem == null)
				return null;
			if (item.Messages.Count == 0)
				return null;
			var availableTokens = GetAvailableTokens(item, null);
			var allmessages = item.Messages
				// Exclude preview messages from the history.
				//.Where(x => !x.IsPreview)
				.SelectMany(x => ConvertMessageItemToChatMessage(item.IsSystemInstructions, x, includeAttachments: null)).ToList();
			var messages = AppHelper.GetMessages(allmessages, availableTokens, ChatLogOptions);
			// Create a copy in order not to add to existing list.
			try
			{
				// Add instructions to generate title to existing messages.
				messages.Add(new chat_completion_message(message_role.system, rItem.TextInstructions));
				var client = new Companions.ChatGPT.Client(item.AiService);
				var maxInputTokens = Client.GetMaxInputTokens(rItem);
				// Send body and context data. Make sure it runs on NON-UI thread.
				var response = await Task.Run(async () => await client.QueryAI(
					rItem.AiModel,
					messages,
					rItem.Creativity,
					item,
					maxInputTokens,
					null
				)).ConfigureAwait(true);
				var body = response.FirstOrDefault()?.Body;
				return body;
			}
			catch (Exception ex)
			{
				AddException(item, ex);
			}
			return null;
		}

		#endregion

		public static void AddException(TemplateItem item, Exception ex)
		{
			var message = ex.Message;
			// Workaround: Provide a hint until Microsoft's OpenAI packages are no longer in beta.
			if (message.Contains("Method not found") && message.Contains("System.Collections.Generic.IAsyncEnumerable"))
				message += " " + Engine.Resources.Resources.VsExtensionVersionMessage;
			var msgItem = new MessageItem(SystemName, message, MessageType.Error);
			msgItem.Attachments.Add(new MessageAttachments(ContextType.Error, "log", ex.ToString()));
			Global.MainControl.Dispatcher.Invoke(() =>
			{
				item.Messages.Add(msgItem);
				item.Modified = DateTime.Now;
			});
		}

		/// <summary>
		/// Set data to Visual Studio.
		/// </summary>
		/// <param name="item"></param>
		/// <param name="data"></param>
		public static void SetData(TemplateItem item, string data)
		{
			if (item.AttachContext == ContextType.Selection && Global.IsVsExtension)
			{
				var vsData = AppHelper.GetMacroValues();
				var code = AppHelper.GetCodeFromReply(data);
				if (item.AutoOperation == DataOperation.Replace)
					Global._SolutionHelper.SetSelection(code);
				if (item.AutoOperation == DataOperation.InsertBefore)
					Global._SolutionHelper.SetSelection(code + vsData.Selection.ContentData);
				if (item.AutoOperation == DataOperation.InsertAfter)
					Global._SolutionHelper.SetSelection(vsData.Selection.ContentData + code);
				if (item.AutoFormatCode)
					Global._SolutionHelper.EditFormatSelection();
			}
			else if (item.AttachContext == ContextType.ActiveDocument && Global.IsVsExtension)
			{
				var vsData = AppHelper.GetMacroValues();
				var code = AppHelper.GetCodeFromReply(data);
				if (item.AutoOperation == DataOperation.Replace)
					Global._SolutionHelper.SetCurrentDocumentContents(code);
				if (item.AutoOperation == DataOperation.InsertBefore)
					Global._SolutionHelper.SetCurrentDocumentContents(code + vsData.Selection.ContentData);
				if (item.AutoOperation == DataOperation.InsertAfter)
					Global._SolutionHelper.SetCurrentDocumentContents(vsData.Selection.ContentData + code);
				if (item.AutoFormatCode)
					Global._SolutionHelper.EditFormatDocument();
			}
		}

		public static int CountTokens(object item, JsonSerializerOptions options)
		{
			var json = JsonSerializer.Serialize(item, options);
			int count;
			List<string> tokens = null;
			GetTokens(json, out count, ref tokens);
			return count;
		}


		public static void GetTokens(string text, out int count, ref List<string> tokens)
		{
			count = 0;
			// Can be `\r\n`, `\r` or `\n`.
			var newLine = FileHelper.GetNewLineType(text).ToArray();
			char? prevChar;
			char? currChar = null;
			var currentToken = "";
			for (int i = 0; i < text.Length; i++)
			{
				prevChar = currChar;
				currChar = text[i];
				if (i == 0)
				{
					currentToken += currChar;
					continue;
				}
				// If letter type changed.
				var flush = true;
				if (char.IsLetterOrDigit(prevChar.Value) && char.IsLetterOrDigit(currChar.Value))
					flush = false;
				else if (prevChar == currChar)
					flush = false;
				else if (newLine.Length == 2 && currChar == newLine[1])
					flush = false;
				if (flush)
				{
					tokens?.Add(currentToken);
					count++;
					currentToken = "";
				}
				currentToken += currChar;
			}
			if (currentToken.Length > 0)
			{
				tokens?.Add(currentToken);
				count++;
			}
		}

	}
}
