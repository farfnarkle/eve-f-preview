using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EveFPreview.Configuration;
using EveFPreview.Services;

namespace EveFPreview.View
{
	/// <summary>
	/// Wine compatibility mode: no DWM, so the thumbnail is a periodically captured still image
	/// stretched over the window instead of a live preview.
	/// </summary>
	sealed class StaticThumbnailView : ThumbnailView
	{
		#region Private fields
		private readonly Image _thumbnail;
		#endregion

		public StaticThumbnailView(IWindowManager windowManager, IThumbnailConfiguration config, IThumbnailManager thumbnailManager, ICharacterPortraitService characterPortraitService)
			: base(windowManager, config, thumbnailManager, characterPortraitService)
		{
			this._thumbnail = new Image
			{
				Stretch = Stretch.Fill,
				// Mouse input belongs to the window underneath (drag, click-to-activate, ...).
				IsHitTestVisible = false
			};
			RenderOptions.SetBitmapScalingMode(this._thumbnail, BitmapScalingMode.HighQuality);
			this.Content = new Grid { Children = { this._thumbnail } };
		}

		protected override void RefreshThumbnail(bool forceRefresh)
		{
			// The base constructor refreshes before this constructor has created the image.
			if (!forceRefresh || this.IsPreventPreviews() || this._thumbnail == null)
			{
				return;
			}

			BitmapSource thumbnail = this.WindowManager.GetStaticThumbnail(this.Id);
			if (thumbnail != null)
			{
				this._thumbnail.Source = thumbnail;
			}
		}

		protected override void ResizeThumbnail(int baseWidth, int baseHeight, int highlightWidthTop, int highlightWidthRight, int highlightWidthBottom, int highlightWidthLeft)
		{
			if (this._thumbnail == null)
			{
				return;
			}

			// The highlight insets are in pixels; the image is laid out in WPF units.
			Matrix fromDevice = (PresentationSource.FromVisual(this) as HwndSource)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
			var margin = new Thickness(
				highlightWidthLeft * fromDevice.M11,
				highlightWidthTop * fromDevice.M22,
				highlightWidthRight * fromDevice.M11,
				highlightWidthBottom * fromDevice.M22);

			if (this._thumbnail.Margin != margin)
			{
				this._thumbnail.Margin = margin;
			}
		}
	}
}
