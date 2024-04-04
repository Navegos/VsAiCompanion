﻿using Embeddings;
using JocysCom.ClassLibrary;
using JocysCom.VS.AiCompanion.DataClient;
using JocysCom.VS.AiCompanion.Engine.Companions.ChatGPT;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using JocysCom.ClassLibrary.Configuration;
using JocysCom.VS.AiCompanion.DataFunctions;
using Embeddings.Embedding;
using Microsoft.ML;
using JocysCom.ClassLibrary.Security;
using System.Threading;
using System.Text;





#if NETFRAMEWORK
using System.Data.SqlClient;
using System.Data.SQLite;
#else
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
#endif

namespace JocysCom.VS.AiCompanion.Engine
{
	public class EmbeddingHelper
	{
		//public class MyDbConfiguration : DbConfiguration
		//{
		//	public MyDbConfiguration(string connectionString)
		//	{
		//		if (connectionString.Contains(".db"))
		//		{
		//			SetProviderFactory("System.Data.SQLite.EF6", SQLiteProviderFactory.Instance);
		//			SetProviderServices("System.Data.SQLite.EF6", (DbProviderServices)SQLiteProviderFactory.Instance.GetService(typeof(DbProviderServices)));
		//		}
		//		else
		//		{
		//			SetProviderServices("System.Data.SqlClient", SqlProviderServices.inInstance);
		//		}
		//	}
		//}

		public static EmbeddingsContext NewEmbeddingsContext(string connectionString)
		{
			var isPortable = SqlInitHelper.IsPortable(connectionString);
#if NETFRAMEWORK
			//DbConnection connection = isPortable
			//	? (DbConnection)new SQLiteConnection(connectionString)
			//	: new SqlConnection(connectionString);
			var db = new EmbeddingsContext();
			db.Database.Connection.ConnectionString = connectionString;
#else
			var optionsBuilder = new DbContextOptionsBuilder<EmbeddingsContext>();
			if (isPortable)
				optionsBuilder.UseSqlite(connectionString);
			else
				optionsBuilder.UseSqlServer(connectionString);
			var db = new EmbeddingsContext(optionsBuilder.Options);
#endif
			return db;
		}

		public static async Task<ProgressStatus> UpdateEmbedding(
			EmbeddingsContext db,
			string fileName,
			System.Security.Cryptography.SHA256 algorithm,
			AiService service, string modelName,
			string embeddingGroupName, EmbeddingGroup embeddingGroupFlag,
			CancellationToken cancellationToken = default
		)
		{
			var fi = new FileInfo(fileName);
			var file = db.Files.FirstOrDefault(x =>
				// Select item by index.
				x.GroupName == embeddingGroupName &&
				x.GroupFlag == (int)embeddingGroupFlag &&
				x.Url == fi.FullName);
			var fileHash = HashHelper.GetHashFromFile(algorithm, fileName);
			var fileHashDb = EmbeddingBase.GetHashByName(file?.Hash, file?.HashType);
			// If file found but different.
			if (fileHashDb != null && !fileHashDb.SequenceEqual(fileHash))
			{
				// Remove parts and file.
				var filePartsToDelete = db.FileParts.Where(x => x.FileId == file.Id).ToList();
				foreach (var filePartToDelete in filePartsToDelete)
					db.FileParts.Remove(filePartToDelete);
				await db.SaveChangesAsync();
				db.Files.Remove(file);
				file = null;
			}
			// If there is no such file in database then...
			if (file == null)
			{
				// Add new file.
				file = new Embeddings.Embedding.File();
				db.Files.Add(file);
				file.Name = fi.Name;
				file.Url = fi.FullName;
				file.GroupName = embeddingGroupName;
				file.GroupFlag = (long)embeddingGroupFlag;
				file.HashType = EmbeddingBase.SHA2_256;
				file.Hash = fileHash;
				file.Size = fi.Length;
				file.IsEnabled = true;
				file.Created = fi.CreationTime.ToUniversalTime();
				file.Modified = fi.LastWriteTime.ToUniversalTime();
			}
			file.State = (int)ProgressStatus.Completed;
			await db.SaveChangesAsync();
			// Process parts.
			var aiModel = Global.AppSettings.AiModels.FirstOrDefault(x => x.AiServiceId == service.Id && x.Name == modelName);
			var parts = GetParts(fi.FullName, aiModel.MaxInputTokens == 0 ? 2048 : aiModel.MaxInputTokens);
			var input = parts.Select(x => x.Text);
			// GetPart hashed from the database.
			var targetFileParts = db.FileParts
				.Where(x => x.FileId == file.Id)
				.OrderBy(x => x.Index)
				.ToList();
			var targetFilePartHashes = targetFileParts
				.Select(x => EmbeddingBase.GetHashByName(x.Hash, x.HashType))
				.ToList();
			var sourceFilePartHashes = input
				.Select(x => algorithm.ComputeHash(System.Text.Encoding.Unicode.GetBytes(x)))
				.ToList();
			// If all hashes match then...
			if (targetFilePartHashes.Count == sourceFilePartHashes.Count &&
				!targetFilePartHashes.Where((x, i) => !x.SequenceEqual(sourceFilePartHashes[i])).Any())
			{
				foreach (var targetFilePart in targetFileParts)
					targetFilePart.State = (int)ProgressStatus.Completed;
				await db.SaveChangesAsync();
				return ProgressStatus.Skipped;
			}
			// Remove parts.
			foreach (var targetFilePart in targetFileParts)
				db.FileParts.Remove(targetFilePart);
			await db.SaveChangesAsync();
			// Fill with updated records.
			var client = new Client(service);
			var results = await client.GetEmbedding(modelName, input, cancellationToken);
			if (cancellationToken.IsCancellationRequested)
				return ProgressStatus.Canceled;
			var now = DateTime.Now;
			foreach (var key in results.Keys)
			{
				var ipart = parts[key];
				var vectors = results[key];
				var part = new Embeddings.Embedding.FilePart();
				part.Embedding = SqlInitHelper.VectorToBinary(vectors);
				part.GroupName = embeddingGroupName;
				part.GroupFlag = (long)embeddingGroupFlag;
				part.FileId = file.Id;
				part.EmbeddingModel = modelName;
				part.EmbeddingSize = vectors.Length;
				part.GroupFlag = (int)embeddingGroupFlag;
				part.Index = 0;
				part.Count = 1;
				part.HashType = EmbeddingBase.SHA2_256;
				part.Hash = sourceFilePartHashes[key];
				part.IsEnabled = true;
				file.State = (int)ProgressStatus.Completed;
				part.Created = now.ToUniversalTime();
				part.Modified = now.ToUniversalTime();
				part.Text = ipart.Text;
				part.TextTokens = ipart.TextTokens;
				db.FileParts.Add(part);
			}
			await db.SaveChangesAsync();
			return ProgressStatus.Updated;
		}

		public string Log { get; set; } = "";
		public List<Embeddings.Embedding.File> Files { get; set; }
		public List<Embeddings.Embedding.FilePart> FileParts { get; set; }

		public static FilePart[] GetParts(string path, int maxTokensPerChunk)
		{
			var mlContext = new MLContext();
			var content = System.IO.File.ReadAllText(path);
			if (string.IsNullOrEmpty(content))
				return Array.Empty<FilePart>();
			var data = new List<TextData> { new TextData { Text = content } };
			var dataView = mlContext.Data.LoadFromEnumerable(data);
			// Tokenize the text
			// Define your custom list of separators
			char[] separators = new char[] { ' ', ',', '.', ';', ':', '!', '?', '-', '(', ')', '[', ']', '{', '}', '\"', '\'', '\n', '\t' };
			var textPipeline = mlContext.Transforms.Text.TokenizeIntoWords("Tokens", nameof(TextData.Text), separators);
			var textTransformer = textPipeline.Fit(dataView);
			var transformedData = textTransformer.Transform(dataView);
			var tokens = mlContext.Data.CreateEnumerable<TokenizedTextData>(transformedData, reuseRowObject: false).First().Tokens;
			// Chunk the tokens
			var chunks = ChunkTokens(tokens, maxTokensPerChunk);
			return chunks.Select(x => new FilePart()
			{
				Text = string.Join(" ", x),
				TextTokens = x.Length

			}).ToArray();
		}

		static IEnumerable<string[]> ChunkTokens(string[] tokens, int maxTokensPerChunk)
		{
			for (int i = 0; i < tokens.Length; i += maxTokensPerChunk)
			{
				yield return tokens.Skip(i).Take(maxTokensPerChunk).ToArray();
			}
		}

		class TextData
		{
			public string Text { get; set; }
		}

		class TokenizedTextData
		{
			public string[] Tokens { get; set; }
		}

		public async Task<string> SearchEmbeddingsToSystemMessage(EmbeddingsItem item, EmbeddingGroup groupFlag, string message, int skip, int take)
		{
			await SearchEmbeddings(item, groupFlag, message, item.Skip, item.Take);
			var systemMessage = "";
			if (FileParts == null || FileParts.Count == 0)
				return systemMessage;
			systemMessage += item.Instructions;
			systemMessage += "\r\n\r\n";
			foreach (var filPart in FileParts)
			{
				var file = Files.Where(x => x.Id == filPart.FileId).FirstOrDefault();
				systemMessage += "\r\n";
				systemMessage += ConvertToChunkString(file?.Url, filPart.Text);
				systemMessage += "\r\n\r\n";
			}
			return systemMessage;
		}


		/// <summary>
		/// Convert to flat string representation.
		/// </summary>
		public static string ConvertToChunkString(string path, string content)
		{
			var sb = new StringBuilder();
			sb.AppendLine($"=== BEGIN FILE CHUNK: {path} ===");
			sb.Append(content);
			if (!content.EndsWith(Environment.NewLine))
				sb.AppendLine();
			sb.AppendLine($"=== END FILE CHUNK: {path} ===");
			return sb.ToString();
		}


		public async Task SearchEmbeddings(EmbeddingsItem item, EmbeddingGroup groupFlag, string message, int skip, int take)
		{
			try
			{
				Log = "Converting message to embedding vectors...";
				if (string.IsNullOrWhiteSpace(message))
				{
					Log += " Message is empty.\r\n";
					return;
				}
				var input = new List<string> { message };
				var client = new Client(item.AiService);
				var results = await client.GetEmbedding(item.AiModel, input);
				Log += " Done.\r\n";
				var target = AssemblyInfo.ExpandPath(item.Target);
				var connectionString = SqlInitHelper.IsPortable(target)
					? SqlInitHelper.PathToConnectionString(target)
					: target;
				var db = NewEmbeddingsContext(connectionString);
				var vectors = results[0];
				Log += "Searching on database...";
				if (SqlInitHelper.IsPortable(item.Target))
				{
					var ids = await SqlInitHelper.GetSimilarFileEmbeddings(connectionString, item.EmbeddingGroupName, groupFlag, vectors, item.Take);
					FileParts = db.FileParts
						.Where(x => ids.Contains(x.Id))
						.ToList()
						.OrderBy(x => ids.IndexOf(x.Id))
						.ToList();
				}
				else
				{
					// Convert your embedding to the format expected by SQL Server.
					// This example assumes `results` is the embedding in a suitable binary format.
					var embeddingParam = new SqlParameter("@promptEmbedding", SqlDbType.VarBinary)
					{
						Value = SqlInitHelper.VectorToBinary(vectors)
					};
					var skipParam = new SqlParameter("@skip", SqlDbType.Int) { Value = skip };
					var takeParam = new SqlParameter("@take", SqlDbType.Int) { Value = take };
					// Assuming `FileSimilarity` is the result type.
					var sqlCommand = "EXEC [Embedding].[sp_getSimilarFileEmbeddings] @promptEmbedding, @skip, @take";
#if NETFRAMEWORK
					FileParts = db.Database.SqlQuery<Embeddings.Embedding.FilePart>(
						sqlCommand, embeddingParam, skipParam, takeParam)
						.ToList();
#else
					FileParts = db.FileParts.FromSqlRaw(
						sqlCommand, embeddingParam, skipParam, takeParam)
						.ToList();
#endif

				}
				var fileIds = FileParts.Select(x => x.FileId).Distinct().ToArray();
				Files = db.Files.Where(x => fileIds.Contains(x.Id)).ToList();
				Log += " Done...";
			}
			catch (System.Exception ex)
			{
				Log = ex.ToString();
			}
		}


		#region Client

		//// Synchronous method to get embeddings
		//public static float[] GetEmbeddings(string url, string apiKey, string text, AiService service)
		//{
		//	if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(text))
		//		throw new ArgumentException("URL, API key, and text cannot be null or empty.");
		//	using (var _httpClient = new HttpClient())
		//	{
		//		// Add the necessary headers to the request
		//		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
		//		// Create the JSON body
		//		var payload = new payload { text = text };
		//		var json = Client.Serialize(payload);
		//		var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
		//		// Make the POST request
		//		var response = _httpClient.PostAsync(url, httpContent).Result;
		//		response.EnsureSuccessStatusCode();
		//		// Read the response as a string
		//		string responseString = response.Content.ReadAsStringAsync().Result;
		//		// Deserialize the response into a list of embeddings
		//		var result = Client.Deserialize<embedding_response>(responseString);
		//		var embeddings = result.embeddings[0].embedding;
		//		return embeddings;
		//	}
		//}



		private class @payload
		{
			public string text { get; set; }
		}

		// Helper class to deserialize the JSON response
		private class @embedding_response
		{
			public Embedding[] embeddings { get; set; }
		}

		private class Embedding
		{
			public float[] embedding { get; set; }
		}


		#endregion
	}
}
