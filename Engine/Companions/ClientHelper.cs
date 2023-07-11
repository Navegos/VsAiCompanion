﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System;
using JocysCom.ClassLibrary.Controls.Chat;
using JocysCom.VS.AiCompanion.Engine.Companions.ChatGPT;
using OpenAI;
using System.Text.Json.Serialization;

namespace JocysCom.VS.AiCompanion.Engine.Companions
{
	public static class ClientHelper
	{

		public const string PreviewModeMessage = "Preview Mode - Sending messages to AI is suppressed.";

		public async static Task Send(TemplateItem item)
		{
			if (Global.IsIncompleteSettings())
				return;
			if (string.IsNullOrEmpty(item.AiModel))
			{
				Global.MainControl.InfoPanel.SetWithTimeout(MessageBoxImage.Warning, "Please select an AI model from the dropdown.");
				return;
			}
			var m = new MessageItem("User", item.Text, MessageType.Out);
			m.BodyInstructions = item.TextInstructions;
			// If task panel then allow to use AutoClear.
			var isTask = Global.Tasks.Items.Contains(item);
			var vsData = AppHelper.GetMacroValues();
			if (item.UseMacros)
			{
				m.BodyInstructions = AppHelper.ReplaceMacros(m.BodyInstructions, vsData);
				m.Body = AppHelper.ReplaceMacros(m.Body, vsData);
			}
			var fileItems = new List<DocItem>();
			var at = item.AttachContext;
			// If data from clipboard.
			if (at.HasFlag(AttachmentType.Clipboard))
			{
				var clip = AppHelper.GetClipboard();
				var clipAttachment = new MessageAttachments()
				{
					Title = Global.AppSettings.ContextDataTitle,
					Type = item.AttachContext,
					Data = clip.Data,
				};
				m.Attachments.Add(clipAttachment);
			}
			// If text selection in Visual Studio.
			if (at.HasFlag(AttachmentType.Selection))
			{
				var ad = Global.GetSelection();
				var adAttachment = new MessageAttachments(AttachmentType.Selection, ad.Language, ad.Data);
				m.Attachments.Add(adAttachment);
			}
			// If selected error in Visual Studio.
			if (at.HasFlag(AttachmentType.Error))
			{
				var err = Global.GetSelectedError();
				if (!string.IsNullOrEmpty(err?.Description))
				{
					var errorAttachment = new MessageAttachments(AttachmentType.Error, err);
					m.Attachments.Add(errorAttachment);
				}
			}
			// If active open document in Visual Studio.
			if (at.HasFlag(AttachmentType.ActiveDocument))
			{
				var ad = Global.GetActiveDocument();
				var adAttachment = new MessageAttachments(AttachmentType.ActiveDocument, ad.Language, ad.Data);
				m.Attachments.Add(adAttachment);
			}
			if (at.HasFlag(AttachmentType.SelectedDocuments))
				fileItems.AddRange(Global.GetSelectedDocuments());
			if (at.HasFlag(AttachmentType.ActiveProject))
				fileItems.AddRange(Global.GetActiveProject());
			if (at.HasFlag(AttachmentType.SelectedProject))
				fileItems.AddRange(Global.GetSelectedProject());
			if (at.HasFlag(AttachmentType.Solution))
				fileItems.AddRange(Global.GetSolution());
			if (at.HasFlag(AttachmentType.ErrorDocument))
				fileItems.Add(Global.GetSelectedErrorDocument());
			if (at.HasFlag(AttachmentType.Exception))
			{
				var ei = Global.GetCurrentException();
				if (!string.IsNullOrEmpty(ei?.Message))
				{
					var exceptionAttachment = new MessageAttachments(AttachmentType.Exception, ei);
					m.Attachments.Add(exceptionAttachment);
				}
			}
			if (at.HasFlag(AttachmentType.ExceptionDocuments))
			{
				// Get files for exception.
				var exceptionFiles = Global.GetCurrentExceptionDocuments();
				// Extract files if exception info was pasted manually inside the message.
				var messagePaths = AppHelper.ExtractFilePaths(item.Text);
				var uniquePaths = messagePaths
					.Where(x => exceptionFiles.All(y => !x.Equals(y.FullName, StringComparison.OrdinalIgnoreCase)));
				var messageFiles = uniquePaths.Select(x => new DocItem(null, x)).ToList();
				fileItems.AddRange(exceptionFiles);
				fileItems.AddRange(messageFiles);
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
			var messageForAI = $"{m.BodyInstructions}\r\n\r\n{m.Body}";
			var chatLogForAI = "";
			var chatLogMessages = new List<ChatCompletionRequestMessage>();
			var maxTokens = Client.GetMaxTokens(item.AiModel);
			var usedTokens = CountTokens(messageForAI);
			var reqTokens = CountTokens(messageForAI);
			// Mark message as preview is preview.
			m.IsPreview = item.IsPreview;
			// Attach chat history at the end (use left tokens).
			if (item.AttachChatHistory)
			{
				var a0 = new MessageAttachments();
				a0.Title = Global.AppSettings.ContextChatTitle;
				a0.Instructions = Global.AppSettings.ContextChatInstructions;
				a0.Type = AttachmentType.ChatHistory;
				var options = new JsonSerializerOptions();
				options.WriteIndented = true;
				if (item.Messages == null)
					item.Messages = new BindingList<MessageItem>();
				var messagesIncludingLast = item.Messages.ToList();
				messagesIncludingLast.Add(m);
				var messages = messagesIncludingLast
					// Include only chat messages.
					.Where(x => x.Type == MessageType.Out || x.Type == MessageType.In)
					// Exclude all preview messages.
					.Where(x => !x.IsPreview)
					.Select(x => new MessageHistoryItem()
					{
						Date = x.Date,
						User = x.User,
						Body = $"{x.BodyInstructions}".Trim().Length == 0
							? $"{x.Body}"
							: $"{x.BodyInstructions}\r\n\r\n{x.Body}",
						Type = x.Type,
					}).ToList();
				// Split 90%/10% between request and response.
				var maxRequesTokens = maxTokens / 2;
				var availableTokens = maxRequesTokens - usedTokens;
				var chatLogOptions = new JsonSerializerOptions
				{
					WriteIndented = true,
					// Serialize enums as string for AI to understand.
					Converters = { new JsonStringEnumConverter() }
				};

				var messagesToSend = AppHelper.GetMessages(messages, availableTokens, chatLogOptions);
				// Attach message body to the bottom of the chat instead.
				messageForAI = "";
				// Prepare messages for API.
				chatLogMessages = messagesToSend.Select(x =>
				{
					return new ChatCompletionRequestMessage()
					{
						Name = x.User,
						Content = x.Body,
						Role = x.Type == MessageType.Out
							? ChatCompletionRequestMessageRole.user
							: ChatCompletionRequestMessageRole.assistant,
					};
				}).ToList();
				var json = JsonSerializer.Serialize(messagesToSend, chatLogOptions);
				a0.Data = $"```json\r\n{json}\r\n```";
				a0.IsMarkdown = true;
				m.Attachments.Add(a0);
			}
			foreach (var a in m.Attachments)
			{
				var aText = "";
				aText += $"\r\n\r\n{a.Title}";
				if (!string.IsNullOrEmpty(a.Instructions))
					aText += $"\r\n\r\n{a.Instructions}";
				aText += $"\r\n\r\n{a.Data}";
				aText = aText.Trim('\r', '\n');
				if (a.Type == AttachmentType.ChatHistory)
					chatLogForAI += aText;
				else
					messageForAI += aText;
			}
			// ShowSensitiveDataWarning
			if (fileItems.Count > 0 && Global.AppSettings.ShowDocumentsAttachedWarning)
			{
				var lines = new List<string>();
				foreach (var fileItem in fileItems)
				{
					if (string.IsNullOrEmpty(fileItem.Data))
						continue;
					var word = AppHelper.ContainsSensitiveData(fileItem.Data);
					if (string.IsNullOrEmpty(word))
						continue;
					lines.Add($"Word '{word}' in File: {fileItem.FullName}\r\n");
				}
				if (lines.Count > 0)
				{
					var text = "Possible sensitive data has been detected. Do you want to send these files to AI?";
					text += "\r\n\r\n" + string.Join("\r\n", lines);
					var caption = $"{Global.Info.Product} - Send Files";
					var result = MessageBox.Show(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
					if (result != MessageBoxResult.Yes)
						return;
				}
			}
			// ShowDocumentsAttachedWarning
			if (fileItems.Count > 0 && Global.AppSettings.ShowDocumentsAttachedWarning)
			{
				var text = "Do you want to send these files to AI?";
				var files = fileItems.Select(x => x.FullName).ToList();
				text += "\r\n\r\n" + string.Join("\r\n", files);
				var caption = $"{Global.Info.Product} - Send Files";
				var result = MessageBox.Show(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
				if (result != MessageBoxResult.Yes)
					return;
			}
			// Message is added. Cleanup now.
			if (isTask)
			{
				if (item.MessageBoxOperation == MessageBoxOperation.ClearMessage)
					item.Text = "";
				if (item.MessageBoxOperation == MessageBoxOperation.ResetMessage)
				{
					var template = Global.GetItems(ItemType.Template).Where(x => x.Name == item.TemplateName).FirstOrDefault();
					if (template != null)
						item.Text = template.Text;
				}
			}
			// Add the message item to the message list once all the content is added.
			// Adding the message will trigger an event that serializes and adds this message to the Chat HTML page.
			item.Messages.Add(m);
			var msgTokens = CountTokens(messageForAI);
			if (item.IsPreview)
			{
				var message = new MessageItem("System", PreviewModeMessage);
				item.Messages.Add(message);
			}
			else if (maxTokens < msgTokens)
			{
				var message = new MessageItem("System", $"Message is too big. Message Tokens: {msgTokens}, Maximum Tokens: {maxTokens}", MessageType.Error);
				item.Messages.Add(message);
			}
			else
			{
				try
				{
					var client = new Companions.ChatGPT.Client(Global.AppSettings.OpenAiSettings.BaseUrl);
					// Send body and context data.
					var response = await client.QueryAI(
						item.AiModel,
						messageForAI,
						chatLogForAI,
						chatLogMessages,
						item.Creativity,
						item
					);
					if (response != null)
					{
						var message = new MessageItem("AI", response, MessageType.In);
						item.Messages.Add(message);
						if (item.AttachContext == AttachmentType.Selection && Global.SetSelection != null)
						{
							var code = AppHelper.GetCodeFromReply(response);
							if (item.AutoOperation == DataOperation.Replace)
								Global.SetSelection(code);
							if (item.AutoOperation == DataOperation.InsertBefore)
								Global.SetSelection(code + vsData.Selection.Data);
							if (item.AutoOperation == DataOperation.InsertAfter)
								Global.SetSelection(vsData.Selection.Data + code);
							if (item.AutoFormatCode)
								Global.EditFormatSelection();
						}
						else if (item.AttachContext == AttachmentType.ActiveDocument && Global.SetActiveDocument != null)
						{
							var code = AppHelper.GetCodeFromReply(response);
							if (item.AutoOperation == DataOperation.Replace)
								Global.SetActiveDocument(code);
							if (item.AutoOperation == DataOperation.InsertBefore)
								Global.SetActiveDocument(code + vsData.Selection.Data);
							if (item.AutoOperation == DataOperation.InsertAfter)
								Global.SetActiveDocument(vsData.Selection.Data + code);
							if (item.AutoFormatCode)
								Global.EditFormatDocument();
						}

					}
				}
				catch (Exception ex)
				{
					var message = new MessageItem("System", ex.Message, MessageType.Error);
					item.Messages.Add(message);
				}
			}
			// If item type task, then allow to do auto removal.
			if (Global.Tasks.Items.Contains(item) && item.AutoRemove)
				_ = Global.MainControl.Dispatcher.BeginInvoke(new Action(() => { _ = Global.Tasks.Items.Remove(item); }));

		}

		public static int CountTokens(string s)
		{
			int count = 0;
			bool inWord = false;
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				char nextC = i < s.Length - 1 ? s[i + 1] : '\0';
				if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
				{
					if (inWord)
					{
						count++;
						inWord = false;
					}
					if (!char.IsWhiteSpace(c))
					{
						if (c == '-' && char.IsLetter(nextC)) // don't split hyphenated words
							continue;
						// don't split contractions and handle multi-character punctuation
						if (c == '\'' && char.IsLetter(nextC) || c == nextC)
							i++;  // skip next character
						count++; // punctuation is a separate token
					}
				}
				else if (!inWord)
				{
					// start of a new word
					inWord = true;
				}
			}
			if (inWord)
				count++;  // count the last word if the string doesn't end with a punctuation or a whitespace
			return count;
		}


	}
}
