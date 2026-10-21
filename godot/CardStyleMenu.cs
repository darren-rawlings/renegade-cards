using System;
using Godot;
using Renegade.Ui;

namespace Renegade.Cards;

/// <summary>
/// The Style menu for card games: a row of back designs and a row of front designs (each front
/// with its own suits and face cards). Choices are kept in GameSettings.Extra under "back" and
/// "front" by the game.
/// </summary>
public partial class CardStyleMenu : Control
{
	public event Action<CardBack>? BackChosen;
	public event Action<CardFront>? FrontChosen;

	private readonly HudTheme _theme;
	private readonly Button[] _backs = new Button[3];
	private readonly Button[] _fronts = new Button[2];

	public CardStyleMenu(HudTheme theme, CardBack back, CardFront front)
	{
		_theme = theme;
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		var scrim = Widgets.Scrim();
		scrim.GuiInput += e =>
		{
			if (e is InputEventMouseButton { Pressed: true })
				Close();
		};
		AddChild(scrim);

		var panel = Widgets.Panel(theme);
		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 18);
		panel.AddChild(column);
		column.AddChild(Widgets.Label("Style", 40, theme.Text));

		column.AddChild(Widgets.Label("Card backs", 26, theme.TextMuted));
		var backRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		backRow.AddThemeConstantOverride("separation", 12);
		foreach (CardBack b in Enum.GetValues<CardBack>())
		{
			var button = Widgets.Button(CardArt.Name(b), 24, theme.ButtonDark, theme);
			button.CustomMinimumSize = new Vector2(170, 64);
			var choice = b;
			button.Pressed += () => { BackChosen?.Invoke(choice); Mark(choice, null); };
			_backs[(int)b] = button;
			backRow.AddChild(button);
		}
		column.AddChild(backRow);

		column.AddChild(Widgets.Label("Card fronts", 26, theme.TextMuted));
		var frontRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		frontRow.AddThemeConstantOverride("separation", 12);
		foreach (CardFront f in Enum.GetValues<CardFront>())
		{
			var button = Widgets.Button(CardArt.Name(f), 24, theme.ButtonDark, theme);
			button.CustomMinimumSize = new Vector2(200, 64);
			var choice = f;
			button.Pressed += () => { FrontChosen?.Invoke(choice); Mark(null, choice); };
			_fronts[(int)f] = button;
			frontRow.AddChild(button);
		}
		column.AddChild(frontRow);
		column.AddChild(Widgets.Body("Every front design has its own suits and face cards.", 22, theme.TextMuted));

		var done = Widgets.Button("Done", 28, theme.Accent, theme);
		done.CustomMinimumSize = new Vector2(0, 72);
		done.Pressed += Close;
		column.AddChild(done);
		AddChild(panel);
		Widgets.Centred(panel, 640);

		_back = back;
		_front = front;
		Mark(back, front);
		Visible = false;
	}

	private CardBack _back;
	private CardFront _front;

	private void Mark(CardBack? back, CardFront? front)
	{
		if (back is { } b)
			_back = b;
		if (front is { } f)
			_front = f;
		for (int i = 0; i < _backs.Length; i++)
			Widgets.Restyle(_backs[i], i == (int)_back ? _theme.Accent : _theme.ButtonDark, _theme);
		for (int i = 0; i < _fronts.Length; i++)
			Widgets.Restyle(_fronts[i], i == (int)_front ? _theme.Accent : _theme.ButtonDark, _theme);
	}

	public void Open() => Widgets.FadeIn(this);

	public void Close() => Widgets.FadeOut(this);
}
