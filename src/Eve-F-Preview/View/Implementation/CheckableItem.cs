using System;
using System.ComponentModel;

namespace EveFPreview.View
{
	/// <summary>
	/// A row in a checkbox list (WPF's stand-in for WinForms' CheckedListBox). <paramref name="checkedChanged"/>
	/// runs after the user - or code - changes <see cref="IsChecked"/>.
	/// </summary>
	sealed class CheckableItem<T> : INotifyPropertyChanged
	{
		private readonly Action<CheckableItem<T>> _checkedChanged;
		private bool _isChecked;

		public CheckableItem(T value, string text, bool isChecked, Action<CheckableItem<T>> checkedChanged)
		{
			this.Value = value;
			this.Text = text;
			this._isChecked = isChecked;
			this._checkedChanged = checkedChanged;
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public T Value { get; }

		public string Text { get; }

		public bool IsChecked
		{
			get => this._isChecked;
			set
			{
				if (this._isChecked == value)
				{
					return;
				}

				this._isChecked = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.IsChecked)));
				this._checkedChanged?.Invoke(this);
			}
		}
	}
}
