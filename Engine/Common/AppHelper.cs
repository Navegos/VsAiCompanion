﻿using JocysCom.ClassLibrary.Controls;
using JocysCom.ClassLibrary.Controls.Chat;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Text.Json;
using JocysCom.VS.AiCompanion.Engine.Companions;
using System.Text;
using System.Threading.Tasks;
using JocysCom.ClassLibrary.Configuration;
using System.Collections.Concurrent;
using System.Reflection;

namespace JocysCom.VS.AiCompanion.Engine
{
	public static class AppHelper
	{

		public static void SetText(Label label, string name, int count, int updatable = 0)
		{
			var text = $"{count} {name}" + (count == 1 ? "" : "s");
			if (updatable > 0)
				text += $", {updatable} Updatable";
			ControlsHelper.SetText(label, text);
		}

		public static MacroValues GetMacroValues()
		{
			var mv = new MacroValues();
			if (Global.GetSelection != null)
				mv.Selection = Global.GetSelection();
			if (Global.GetActiveDocument != null)
				mv.Document = Global.GetActiveDocument() ?? new DocItem();
			return mv;
		}

		public static List<PropertyItem> GetReplaceMacrosSelection()
		{
			var keys = JocysCom.ClassLibrary.Text.Helper.GetReplaceMacros<DocItem>(true, nameof(MacroValues.Selection));
			return keys.Select(x => new PropertyItem(x)).ToList();
		}

		//public static List<string> GetMacrosOfStartupProject()
		//	=> Global.GetMacrosOfStartupProject().Keys.ToList();

		public static List<PropertyItem> GetReplaceMacrosDocument()
		{
			var keys = JocysCom.ClassLibrary.Text.Helper.GetReplaceMacros<DocItem>(true, nameof(MacroValues.Document));
			return keys.Select(x => new PropertyItem(x)).ToList();
		}

		public static List<PropertyItem> GetReplaceMacrosDate()
		{
			var keys = JocysCom.ClassLibrary.Text.Helper.GetReplaceMacros<DateTime>(true, nameof(MacroValues.Date));
			return keys.Select(x => new PropertyItem(x)).ToList();
		}

		private const string EnvironmentPrefix = "Env";

		public static string ReplaceMacros(string s, MacroValues o)
		{
			s = JocysCom.ClassLibrary.Text.Helper.Replace(s, o.Date, true, nameof(MacroValues.Date));
			s = JocysCom.ClassLibrary.Text.Helper.Replace(s, o.Selection, true, nameof(MacroValues.Selection));
			s = JocysCom.ClassLibrary.Text.Helper.Replace(s, o.Document, true, nameof(MacroValues.Document));
			var envDic = GetEnvironmentProperties().ToDictionary(x => x.Key.Substring(EnvironmentPrefix.Length + 1), x => (object)x.Value);
			s = JocysCom.ClassLibrary.Text.Helper.ReplaceDictionary(s, envDic, true, EnvironmentPrefix);
			return s;
		}

		public static string GetCodeFromReply(string replyText)
		{
			// Try to match code block pattern (triple backticks) with any language
			var codeBlockPattern = @"(?s)[\`]{3}(?<language>.*?)\r?\n(?<code>.*?)[\`]{3}";
			var codeBlockMatch = Regex.Match(replyText, codeBlockPattern, RegexOptions.Singleline);
			// If code block found, return the code inside it
			if (codeBlockMatch.Success)
				return codeBlockMatch.Groups["code"].Value.Trim();
			// Try to match inline code pattern (single backticks)
			var inlineCodePattern = @"`(?<code>.*?)`";
			var inlineCodeMatch = Regex.Match(replyText, inlineCodePattern);
			// If inline code found, return the code inside it
			if (inlineCodeMatch.Success)
				return inlineCodeMatch.Groups["code"].Value;
			// If no code block or inline code found, return the original reply text
			return replyText;
		}

		public static List<PropertyItem> GetEnvironmentProperties()
		{
			var envVars = Environment.GetEnvironmentVariables();
			var solutionProperties = new List<PropertyItem>();
			foreach (DictionaryEntry envVar in envVars)
			{
				if ($"{envVar.Key}".Contains("."))
					continue;
				var solutionProperty = new PropertyItem
				{
					Key = $"{EnvironmentPrefix}.{envVar.Key}",
					Value = $"{envVar.Value}",
					Display = $"{envVar.Value}",
					//Display = $"{envVar.Key} = {envVar.Value}"
				};
				solutionProperties.Add(solutionProperty);
			}
			return solutionProperties.OrderBy(x => x.Key).ToList();
		}

		public static DocItem GetClipboard()
		{
			var text = Clipboard.GetText();
			var item = new DocItem(text);
			item.Name = nameof(Clipboard);
			return item;
		}
		public static void SetClipboard(string text)
		{
			Clipboard.SetText(text);
		}

		public static int CountTokens(object item, JsonSerializerOptions options)
		{
			var json = JsonSerializer.Serialize(item, options);
			return ClientHelper.CountTokens(json);
		}

		/// <summary>
		/// Return lis of messages, but do not exceed availableTokens.
		/// Priorityu of adding messages:
		/// Last message. First message. All mesages beginning from the end.
		/// </summary>
		/// <param name="messages"></param>
		/// <param name="availableTokens"></param>
		public static List<MessageHistoryItem> GetMessages(
			List<MessageHistoryItem> messages,
			int availableTokens,
			JsonSerializerOptions options
		)
		{
			var target = new List<MessageHistoryItem>();
			if (messages.Count == 0)
				return target;
			var source = messages.ToList();
			int currentTokens = 0;
			var firstMessageInChat = source.First();
			// Reverse order (begin adding latest messages first)
			source.Reverse();
			var firstMessageInChatTokens = CountTokens(firstMessageInChat, options);
			for (int i = 0; i < source.Count; i++)
			{
				var item = source[i];
				var itemTokens = CountTokens(item, options);
				var hasSpaceForLastItemAfterAdd = currentTokens + itemTokens + firstMessageInChatTokens < availableTokens;
				// If first item was added already and
				// this is not the last item and 
				// won't be able to last item then...
				if (i > 0 && item != firstMessageInChat && !hasSpaceForLastItemAfterAdd)
				{
					target.Add(firstMessageInChat);
					break;
				}
				var hasSpaceForThisItem = currentTokens + itemTokens < availableTokens;
				if (!hasSpaceForThisItem)
					break;
				target.Add(item);
			}
			// Reverse the result to maintain the original order
			target.Reverse();
			return target;
		}

		public static System.Drawing.Image ConvertDrawingImageToDrawingBitmap(DrawingImage drawingImage, int targetWidth, int targetHeight)
		{
			// Create a BitmapSource from the DrawingImage
			double dpi = 96;
			RenderTargetBitmap renderTarget = new RenderTargetBitmap(targetWidth, targetHeight, dpi, dpi, PixelFormats.Pbgra32);
			DrawingVisual drawingVisual = new DrawingVisual();

			using (DrawingContext context = drawingVisual.RenderOpen())
			{
				context.DrawImage(drawingImage, new Rect(new System.Windows.Point(), new System.Windows.Size(targetWidth, targetHeight)));
			}

			renderTarget.Render(drawingVisual);
			BitmapSource bitmapSource = BitmapFrame.Create(renderTarget);

			// Convert the BitmapSource to a Bitmap
			System.Drawing.Bitmap bitmap;
			using (MemoryStream outStream = new MemoryStream())
			{
				BitmapEncoder encoder = new PngBitmapEncoder();
				encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
				encoder.Save(outStream);
				bitmap = new System.Drawing.Bitmap(outStream);
			}

			return bitmap;
		}

		public static List<string> ExtractFilePaths(string stackTrace)
		{
			var items = new List<string>();
			if (string.IsNullOrEmpty(stackTrace))
				return items;
			var matchCollection = Regex.Matches(stackTrace, @"in\s(?<name>.*):line\s\d+");
			foreach (Match match in matchCollection)
			{
				var name = match.Groups["name"].Value;
				var isValid = !name.ToCharArray().Intersect(Path.GetInvalidPathChars()).Any();
				if (isValid)
					items.Add(name);
			}
			return items;
		}

		/// <summary>
		/// Fix name to make sure that it is not same as existing names.
		/// </summary>
		public static void FixName(TemplateItem copy, IEnumerable<TemplateItem> items)
		{
			var newName = copy.Name;
			for (int i = 1; i < int.MaxValue; i++)
			{
				var sameFound = items.Any(x => string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase));
				// If item with the same name not found then...
				if (!sameFound)
					break;
				// Change name of the copy and continue.
				newName = $"{copy.Name} ({i})";
				continue;
			}
			if (copy.Name != newName)
				copy.Name = newName;
		}

		public static string ContainsSensitiveData(string contents)
		{
			if (string.IsNullOrEmpty(contents))
				return null;
			List<string> sensitiveWords = new List<string> {
				"password",
				"card number",
				"secret keyword",
				"social security number",
				"credit card",
				"cvv",
				"expiration date",
				"passport number"
			};
			var lower = contents.ToLower();
			foreach (var word in sensitiveWords)
			{
				if (lower.Contains(word))
					return word;
			}
			return null;
		}

		/// <summary>
		/// Helps to generate same Unique IDs.
		/// </summary>
		public static Guid GetGuid(params object[] args)
		{
			var value = string.Join(Environment.NewLine, args);
			var algorithm = System.Security.Cryptography.SHA256.Create();
			// Important: Don’t Use Encoding.Default, because it is different on different machines and send data may be decoded as as gibberish.
			// Use UTF-8 or Unicode (UTF-16), used by SQL Server.
			var encoding = Encoding.UTF8;
			var bytes = encoding.GetBytes(value);
			var hash = algorithm.ComputeHash(bytes);
			var guidBytes = new byte[16];
			Array.Copy(hash, guidBytes, guidBytes.Length);
			Guid guid = new Guid(guidBytes);
			algorithm.Dispose();
			return guid;
		}


		/// <summary>
		/// Download models from API service.
		/// </summary>
		public static async Task UpdateModelsFromAPI(AiService aiService)
		{
			if (Global.IsIncompleteSettings(aiService))
				return;
			Regex filterRx = null;
			try
			{
				filterRx = new Regex(aiService.ModelFilter);
			}
			catch { }
			var client = new Companions.ChatGPT.Client(aiService);
			var models = await client.GetModels();
			var modelCodes = models
				.OrderByDescending(x => x.Id)
				.Select(x => x.Id)
				.ToArray();
			if (filterRx != null)
				modelCodes = modelCodes.Where(x => filterRx.IsMatch(x)).ToArray();
			// If models found then...
			if (modelCodes.Any())
			{
				// Remove all old models.
				var serviceModels = Global.AppSettings.AiModels.Where(x => x.AiServiceId == aiService.Id).ToList();
				foreach (var serviceModel in serviceModels)
					Global.AppSettings.AiModels.Remove(serviceModel);
				// Add all new models.
				foreach (var modelCode in modelCodes)
					Global.AppSettings.AiModels.Add(new AiModel(modelCode, aiService.Id));
				// This will inform all forms that models changed.
				Global.TriggerAiModelsUpdated();
			}
		}

		/// <summary>
		/// Load models ComboBoc source.
		/// </summary>
		/// <param name="extraNames">
		/// Make sure that target list contains extra model names.
		/// </param>
		public static void UpdateModelCodes(AiService aiService, IList<string> target, params string[] extraNames)
		{
			if (aiService == null)
				return;
			// Make sure checkbox can display current model.
			var serviceModels = Global.AppSettings.AiModels
				.Where(x => x.AiServiceId == aiService.Id)
				.Select(x => x.Name)
				.ToList();
			foreach (var extraName in extraNames)
			{
				if (!string.IsNullOrEmpty(extraName) && !serviceModels.Contains(extraName))
					serviceModels.Add(extraName);
			}
			SettingsHelper.Synchronize(serviceModels, target);
		}

		public static TemplateItem GetNewTemplateItem()
		{
			var item = new TemplateItem();
			var defaultAiService = Global.AppSettings.AiServices.FirstOrDefault(x => x.IsDefault) ??
				Global.AppSettings.AiServices.FirstOrDefault(); ;
			item.AiServiceId = defaultAiService?.Id ?? Guid.Empty;
			item.AiModel = defaultAiService.DefaultAiModel;
			return item;
		}

		#region Copy Properties

		public static BindingFlags DefaultBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		public static bool IsKnownType(Type type)
		{
			if (type is null)
				throw new ArgumentNullException(nameof(type));
			return
				type == typeof(string)
				|| type.IsPrimitive
				|| type.IsSerializable;
		}

		/// <summary>Cache data for speed.</summary>
		/// <remarks>Cache allows for this class to work 20 times faster.</remarks>
		private static ConcurrentDictionary<Type, PropertyInfo[]> Properties { get; } = new ConcurrentDictionary<Type, PropertyInfo[]>();

		private static PropertyInfo[] GetProperties(Type t, bool cache = true)
		{
			var items = cache
				? Properties.GetOrAdd(t, x => t.GetProperties(DefaultBindingFlags))
				: t.GetProperties(DefaultBindingFlags);
			return items;
		}

		public static void CopyProperties(object source, object target)
		{
			if (source is null)
				throw new ArgumentNullException(nameof(source));
			if (target is null)
				throw new ArgumentNullException(nameof(target));
			// Get type of the destination object.
			var sourceProperties = GetProperties(source.GetType());
			var targetProperties = GetProperties(target.GetType());
			foreach (var sp in sourceProperties)
			{
				// Get destination property and skip if not found.
				var tp = targetProperties.FirstOrDefault(x => Equals(x.Name, sp.Name));
				if (tp == null || !IsKnownType(sp.PropertyType) || sp.PropertyType != tp.PropertyType)
					continue;
				if (!sp.CanRead || !tp.CanWrite)
					continue;
				// Get source value.
				var sValue = sp.GetValue(source, null);
				var update = true;
				// If can read target value.
				if (tp.CanRead)
				{
					// Get target value.
					var dValue = tp.GetValue(target, null);
					// Update only if values are different.
					update = !Equals(sValue, dValue);
				}
				if (update)
					tp.SetValue(target, sValue, null);
			}
		}

		#endregion

	}

}
