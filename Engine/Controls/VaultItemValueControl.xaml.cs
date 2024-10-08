﻿using JocysCom.ClassLibrary.ComponentModel;
using JocysCom.ClassLibrary.Controls;
using JocysCom.VS.AiCompanion.Engine.Security;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace JocysCom.VS.AiCompanion.Engine.Controls
{
	/// <summary>
	/// Interaction logic for VaultItemValueControl.xaml
	/// </summary>
	public partial class VaultItemValueControl : UserControl, INotifyPropertyChanged
	{
		public VaultItemValueControl()
		{
			InitializeComponent();
			if (ControlsHelper.IsDesignMode(this))
				return;
			PropertyChanged += This_PropertyChanged;
		}

		private void This_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(VaultItemId))
				UpdateItemFromVaultItemId();
			if (e.PropertyName == nameof(Value))
				ControlsHelper.SetText(ValuePasswordBox, Value);
		}

		public SortableBindingList<object> VaultItems1 { get; }
			= new SortableBindingList<object>() { new { Id = (Guid?)null, Name = "" } };

		public SortableBindingList<VaultItem> VaultItems2 { get; set; }

		private async void VaultItemRefreshButton_Click(object sender, RoutedEventArgs e)
		{
			if (ControlsHelper.IsOnCooldown(sender))
				return;
			_ = await MicrosoftResourceManager.Current.RefreshItemFromKeyVaultSecret(Item?.Id);
		}

		public VaultItem Item
		{
			get => _Item;
			set
			{
				if (_Item != null)
				{
					_Item.PropertyChanged -= _Item_PropertyChanged;
				}
				_Item = value;
				if (_Item != null)
				{
					_Item.PropertyChanged += _Item_PropertyChanged;
					ControlsHelper.SetText(VaultItemValuePasswordBox, Item.Value);
				}
			}
		}
		VaultItem _Item;

		private void _Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(VaultItem.Value))
				ControlsHelper.SetText(VaultItemValuePasswordBox, Item.Value);
		}

		#region Properties

		/// <summary>Vault Item ID dependency property.</summary>
		public static readonly DependencyProperty VaultItemIdProperty =
			DependencyProperty.Register(nameof(VaultItemId), typeof(Guid?), typeof(VaultItemValueControl),
				new PropertyMetadata(null, OnVaultItemIdChanged));

		private static void OnVaultItemIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var control = d as VaultItemValueControl;
			control.OnPropertyChanged(nameof(VaultItemId));
		}


		public Guid? VaultItemId
		{
			get { return (Guid?)GetValue(VaultItemIdProperty); }
			set { SetValue(VaultItemIdProperty, value); }
		}

		/// <summary>Value dependency property.</summary>
		public static readonly DependencyProperty ValueProperty =
			DependencyProperty.Register(nameof(Value), typeof(string), typeof(VaultItemValueControl),
			new PropertyMetadata(null, OnValueChanged));

		public string Value
		{
			get { return (string)GetValue(ValueProperty); }
			set { SetValue(ValueProperty, value); }
		}

		private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var control = d as VaultItemValueControl;
			control.OnPropertyChanged(nameof(Value));
		}

		#endregion

		private void ValuePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
		{
			if (Value != ValuePasswordBox.Password)
				Value = ValuePasswordBox.Password;
		}

		void UpdateItemFromVaultItemId()
		{
			Item = Global.AppSettings.VaultItems.FirstOrDefault(x => x.Id == VaultItemId);
			var useVaultItem = VaultItemId != null;
			ValuePasswordBox.Visibility = !useVaultItem
				? Visibility.Visible
				: Visibility.Collapsed;
			var vaultVisibility = useVaultItem
				? Visibility.Visible
				: Visibility.Collapsed;
			VaultItemRefreshButton.Visibility = vaultVisibility;
			VaultItemValuePasswordBox.Visibility = vaultVisibility;
		}


		private void This_Loaded(object sender, RoutedEventArgs e)
		{
			if (ControlsHelper.IsDesignMode(this))
				return;
			if (ControlsHelper.AllowLoad(this))
			{
				VaultItems2 = Global.AppSettings.VaultItems;
				OnPropertyChanged(nameof(VaultItems2));
				ValuePasswordBox.PasswordChanged += ValuePasswordBox_PasswordChanged;
				Global.UserProfile.PropertyChanged += Profile_PropertyChanged;
				AppHelper.InitHelp(this);
				UiPresetsManager.InitControl(this, true);
			}
		}

		private void Profile_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(UserProfile.IsSignedIn))
				OnPropertyChanged(nameof(UserIsSigned));
		}

		public bool UserIsSigned => Global.UserProfile.IsSignedIn;

		#region ■ INotifyPropertyChanged

		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		#endregion

	}
}
