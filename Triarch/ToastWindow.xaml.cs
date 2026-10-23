using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Triarch;

public partial class ToastWindow : Window, IComponentConnector
{
	private readonly DispatcherTimer _hide = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(5.0)
	};

	public ToastWindow()
	{
		InitializeComponent();
		SourceInitialized += (object? _, EventArgs _) =>
		{
			nint handle = new WindowInteropHelper(this).Handle;
			SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x80 | 0x8000000);
		};
		SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			Place();
		};
		_hide.Tick += (object? _, EventArgs _) =>
		{
			_hide.Stop();
			Fade(0.0, () =>
			{
				Hide();
			});
		};
	}

	public void Notify(string title, string text, string icon)
	{
		TTitle.Text = title;
		TText.Text = text;
		TIcon.Text = icon;
		if (!IsVisible)
		{
			Show();
			Place();
		}
		Fade(1.0, null);
		_hide.Stop();
		_hide.Start();
	}

	private void Place()
	{
		Rect wa = SystemParameters.WorkArea;
		Left = wa.Right - ActualWidth - 16.0;
		Top = wa.Bottom - ActualHeight - 16.0;
	}

	private void Fade(double to, Action? done)
	{
		DoubleAnimation anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180.0));
		if (done != null)
		{
			anim.Completed += (object? _, EventArgs _) =>
			{
				done();
			};
		}
		BeginAnimation(UIElement.OpacityProperty, anim);
	}

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(nint h, int index);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong(nint h, int index, int value);
}
