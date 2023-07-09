﻿using System;

namespace JocysCom.ClassLibrary.Controls.Chat
{
	public class MessageHistoryItem
	{
		public DateTime Date {get;set;}
		public string User { get; set; }
		public MessageType Type { get; set; }
		public string Body { get; set; }
	}
}
