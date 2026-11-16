using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Gloria.Services;

/// <summary>
/// Shows what can be clicked: buttons, pickers and anything with a <see cref="TapGestureRecognizer"/> get the hand
/// cursor and light up slightly while the mouse is over them. MAUI has no cursor API, and on Windows its button handler
/// sets the hover brush to the normal background, so this hooks every view's handler once instead of each page's XAML.
/// </summary>
public static class ClickableHover
{
	// A translucent white for elements without a background of their own (ghost buttons, table rows).
	private static readonly Color Overlay = Color.FromRgba(255, 255, 255, 18);
	private const float BackgroundMix = 0.1f;
	private const float TextMix = 0.35f;

	private static readonly PropertyInfo? CursorProperty =
		typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly ConditionalWeakTable<View, Hover> Hooked = [];

	public static void Install() =>
		ViewHandler.ViewMapper.AppendToMapping(nameof(ClickableHover), (handler, view) =>
		{
			if (view is View element && IsClickable(element) && (handler.ContainerView ?? handler.PlatformView) is UIElement platform)
				Hooked.GetValue(element, e => new Hover(e)).Attach(platform);
		});

	private static bool IsClickable(View view) =>
		view is Button or Picker or DatePicker or TimePicker
		|| view.GestureRecognizers.OfType<TapGestureRecognizer>().Any();

	/// <summary>Mixes a background slightly toward white; a missing or transparent one becomes a faint white overlay.</summary>
	private static Color Lighten(Color? color) =>
		color is null || color.Alpha == 0 ? Overlay : Toward(color, Colors.White, BackgroundMix);

	private static Color Toward(Color color, Color target, float amount) =>
		new(
			color.Red + (target.Red - color.Red) * amount,
			color.Green + (target.Green - color.Green) * amount,
			color.Blue + (target.Blue - color.Blue) * amount,
			color.Alpha);

	private sealed class Hover
	{
		private readonly View _view;
		private UIElement? _platform;
		private bool _over;
		private bool _lit;
		private Color? _labelColor;

		public Hover(View view)
		{
			_view = view;
			_view.PropertyChanged += OnViewPropertyChanged;
		}

		public void Attach(UIElement platform)
		{
			if (ReferenceEquals(platform, _platform))
				return;

			_platform = platform;
			// handledEventsToo: a WinUI Button marks pointer events handled.
			platform.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) => SetOver(true)), true);
			platform.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => SetOver(false)), true);
			platform.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => SetOver(false)), true);
			platform.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => SetOver(false)), true);
			_over = false;
			UpdateCursor();
			Update();
		}

		private void SetOver(bool over)
		{
			_over = over;
			Update();
		}

		private void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == VisualElement.IsEnabledProperty.PropertyName)
			{
				UpdateCursor();
				Update();
			}
			else if (_lit && e.PropertyName == VisualElement.BackgroundColorProperty.PropertyName && _view is not Label)
				// The bound color changed under the highlight, e.g. a chip that was just selected.
				_view.Background = new SolidColorBrush(Lighten(_view.BackgroundColor));
		}

		private void UpdateCursor()
		{
			if (_platform is not null)
				CursorProperty?.SetValue(_platform, InputSystemCursor.Create(_view.IsEnabled ? InputSystemCursorShape.Hand : InputSystemCursorShape.Arrow));
		}

		private void Update()
		{
			var lit = _over && _view.IsEnabled;
			if (lit == _lit)
				return;

			_lit = lit;
			if (_view is Label label)
			{
				// A box behind bare link text looks odd, so links brighten their text and underline instead.
				// Clickable labels set TextColor directly, not through a binding, so it can be restored as it was.
				if (lit)
				{
					_labelColor = label.TextColor;
					if (label.TextColor is { } color)
						label.TextColor = Toward(color, Colors.White, TextMix);
					label.TextDecorations = TextDecorations.Underline;
				}
				else
				{
					label.TextColor = _labelColor;
					label.ClearValue(Label.TextDecorationsProperty);
				}
			}
			// Background wins over BackgroundColor, so the highlight leaves bound background colors alone.
			else if (lit)
				_view.Background = new SolidColorBrush(Lighten(_view.BackgroundColor));
			else
				_view.ClearValue(VisualElement.BackgroundProperty);
		}
	}
}
