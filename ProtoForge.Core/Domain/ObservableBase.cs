namespace ProtoForge.Core.Domain
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Runtime.CompilerServices;

	/// <summary>Small INotifyPropertyChanged base so the domain stays independent from any UI toolkit.</summary>
	public abstract class ObservableBase : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;

		protected Boolean SetProperty<T>(ref T storage, T value, [CallerMemberName] String? propertyName = null)
		{
			if (EqualityComparer<T>.Default.Equals(storage, value))
			{
				return false;
			}

			storage = value;
			OnPropertyChanged(propertyName);
			return true;
		}

		protected void OnPropertyChanged([CallerMemberName] String? propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}