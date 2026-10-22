using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Play;
using Renegade.Ui;

namespace Renegade.Cards;

/// <summary>
/// What every Renegade card game shares, so each game is only its rules and how they're shown:
/// the table (2 to 5 seats, or a solitaire layout), the card looks and sounds, the dealer, glass
/// hands and fans at each seat, speech bubbles, the HUD (Style for card backs and fronts, Demo,
/// Send, Start over, Settings with the shop), ads every few games, the winner's fire, the
/// developer flags and checks, and a safe way to run the game's animations as tasks.
/// A game derives from this, fills in the abstract members and starts its rounds in
/// <see cref="NewRound"/>.
/// </summary>
public abstract partial class CardGame : Node3D
{
	public static readonly Color Muted = new("d9b99b");
	public static readonly Color Bright = new("fff3e0");
	public static readonly Color GlassTint = new(0.8f, 0.92f, 1f);
	public static readonly string[] Names = { "You", "Rosie", "Max", "Lena", "Theo" };

	protected readonly Random Random = new();
	protected readonly Scoreboard Wins = new();
	protected GameSettings Settings = null!;
	protected CardTable Table = null!;
	protected CardLooks Looks = null!;
	protected CardSounds Sounds = null!;
	protected Dealer Dealer = null!;
	protected GameHud Hud = null!;
	protected IAdService Ads = null!;
	protected readonly CardFan[] Fans = new CardFan[CardTable.MaxSeats];
	protected readonly SeatHands[] Hands = new SeatHands[CardTable.MaxSeats];

	private CardStyleMenu _styleMenu = null!;
	private AdSchedule? _adSchedule;
	private readonly Label3D[] _bubbles = new Label3D[CardTable.MaxSeats];
	private Flames? _fire;
	private string? _art;
	private bool _handCheck;

	/// <summary>True while the table is acting something out; taps are ignored.</summary>
	protected bool Busy = true;

	/// <summary>Counts rounds, so an animation from an abandoned round can tell it should stop.</summary>
	protected int Round;

	/// <summary>The computer plays every seat (the --demo flag, used for recordings and checks).</summary>
	protected bool Demo;

	protected bool QuitAfterDemo;

	/// <summary>How many sit at the table (from Settings › Players, or the game's only size).</summary>
	protected int Players;

	// ------------------------------------------------------------------ What a game provides

	protected abstract string Title { get; }
	protected abstract string Hint { get; }
	protected abstract IReadOnlyList<TutorialPage> TutorialPages { get; }

	/// <summary>The table sizes the game allows; if they differ, Settings gets a Players choice.</summary>
	protected virtual int MinPlayers => 2;
	protected virtual int MaxPlayers => 2;
	protected virtual int DefaultPlayers => MinPlayers;

	/// <summary>Whether the game has a computer opponent with levels (solitaire games don't).</summary>
	protected virtual bool HasLevels => true;

	/// <summary>Whether players hold their cards in fans with glass hands (solitaire games don't).</summary>
	protected virtual bool HoldsCards => true;

	/// <summary>A solitaire layout: the camera looks more steeply down on a wider felt, with no seats.</summary>
	protected virtual bool Solitaire => false;

	/// <summary>Whether your fan sorts itself by rank (games that arrange it themselves, like gin, say no).</summary>
	protected virtual bool SortYourHand => true;

	protected virtual string NewGameText => "Deal again";

	/// <summary>Adds the game's own rows to Settings (use <see cref="GameHud.AddToggle"/> and AddChoice).</summary>
	protected virtual void AddSettings() { }

	/// <summary>A game's own developer flag (after the shared ones).</summary>
	protected virtual void DevArg(string name, string value) { }

	/// <summary>Starts a new round: clear the table, shuffle, deal and play.</summary>
	protected abstract Task NewRound();

	/// <summary>True once a round has begun and isn't over (Start over then asks first).</summary>
	protected abstract bool InProgress { get; }

	/// <summary>A tap on the table, when nothing is being acted out: a ray from the camera.</summary>
	protected abstract void Tapped(Vector3 origin, Vector3 direction, Vector2 screen);

	/// <summary>The level changed in the Level menu (games rebuild their computer players).</summary>
	protected virtual void LevelChanged(Difficulty difficulty) { }

	// ------------------------------------------------------------------ Start-up

	public override void _Ready()
	{
		DevLog.Tag = Title.Length >= 3 ? Title[..3].ToLowerInvariant() : "crd";
		AudioWatch.Attach(this, Title);
		Settings = GameSettings.Load();
		Players = Math.Clamp(Settings.Extra.TryGetValue("players", out var n) && int.TryParse(n, out int p) ? p : DefaultPlayers, MinPlayers, MaxPlayers);
		ReadDeveloperArgs();
		if (_art != null)
		{
			_ = ArtCheck.Run(this, _art);
			return;
		}
		if (_handCheck)
		{
			Run(ShowHands());
			return;
		}
		AudioServer.SetBusMute(0, Settings.Muted);
		Run(Start());
	}

	private async Task ShowHands() => HandCheck.Build(this, await CardLooks.Create(this, CardFront.Classic, CardBack.Crimson));

	private async Task Start()
	{
		Table = new CardTable { Seats = Solitaire ? 1 : Players, Solitaire = Solitaire };
		AddChild(Table);
		var pool = new SoundPool();
		AddChild(pool);
		Sounds = new CardSounds(pool);
		Dealer = new Dealer(this, Sounds, Random);
		Looks = await CardLooks.Create(this, Saved("front", CardFront.Classic), Saved("back", CardBack.Crimson));

		if (HoldsCards && !Solitaire)
		{
			for (int s = 0; s < Players; s++)
			{
				Fans[s] = s == 0
					? new CardFan(Table.FanPivot[0], sorted: SortYourHand)
					: new CardFan(Table.FanPivot[s], sorted: false) { Radius = 0.5f, Spread = 9f, MaxArc = 80f };
				Hands[s] = new SeatHands(this, Sounds, Table.FanPivot[s], GlassTint);
			}
		}
		for (int s = 0; s < (Solitaire ? 1 : Players); s++)
		{
			_bubbles[s] = new Label3D
			{
				FontSize = 88,
				PixelSize = 0.0042f,
				OutlineSize = 22,
				OutlineModulate = new Color(0.08f, 0.05f, 0.03f, 0.9f),
				Modulate = Bright,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				NoDepthTest = true,
				Position = Table.Bubble[s],
				Visible = false,
			};
			AddChild(_bubbles[s]);
		}

		_adSchedule ??= new AdSchedule(Settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		Ads = AdServices.Create(this, AdUnits, Demo, new PlaceholderAds { TextColor = theme.Text });
		Hud = new GameHud(theme);
		AddChild(Hud);
		Hud.Build(new HudOptions(Title, Hint, Modes: false, LevelOnly: HasLevels, NewGameText: NewGameText,
			FreezeSetting: false, CustomStyle: () => _styleMenu.Open()), Settings);
		Hud.NewGamePressed += OnNewGamePressed;
		Hud.RestartPressed += OnRestartPressed;
		Hud.SetTutorial(TutorialPages);
		Hud.ModeChosen += (mode, difficulty) =>
		{
			Settings.Difficulty = difficulty;
			Settings.Save();
			LevelChanged(difficulty);
		};
		var send = Hud.AddButton("Send", false, Control.LayoutPreset.TopLeft, 244, 344, 148, 196, 22);
		send.Pressed += () => Hud.Dialog("Send to a friend",
			$"Once {Title} is on Google Play, this opens your phone's share menu with a link to the game, so a friend can get it and play you.",
			("OK", true, null));
		if (MaxPlayers > MinPlayers)
		{
			var sizes = Enumerable.Range(MinPlayers, MaxPlayers - MinPlayers + 1).Select(x => x.ToString()).ToArray();
			Hud.AddChoice("Players", sizes, Players - MinPlayers, choice =>
			{
				Settings.Extra["players"] = (choice + MinPlayers).ToString();
				Settings.Save();
				GetTree().ReloadCurrentScene();
			});
		}
		AddSettings();

		_styleMenu = new CardStyleMenu(theme, Looks.Back, Looks.Front);
		_styleMenu.BackChosen += back => { _ = Looks.SetBack(this, back); Save("back", back); };
		_styleMenu.FrontChosen += front => { _ = Looks.SetFront(this, front); Save("front", front); };
		Hud.AddChild(_styleMenu);
		if (!Demo)
			Hud.OfferTutorialOnce(Settings);
		Hud.ApplyDevFlags();
		await NewRound();
	}

	/// <summary>Google's test ad units until each game's real ones go in (see the release playbook).</summary>
	protected virtual AdMobConfig AdUnits => AdMobConfig.Test;

	// ------------------------------------------------------------------ Helpers for games

	/// <summary>Runs a task without waiting for it, but reports anything that goes wrong (otherwise it would vanish).</summary>
	protected static async void Run(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception error)
		{
			GD.PrintErr($"Card game: {error}");
			DevLog.Write($"error: {error.Message}");
		}
	}

	protected async Task Pause(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	protected async Task Await(Tween? tween)
	{
		if (tween != null && tween.IsValid() && tween.IsRunning())
			await ToSignal(tween, Tween.SignalName.Finished);
	}

	protected T Saved<T>(string key, T fallback) where T : struct, Enum =>
		Settings.Extra.TryGetValue(key, out var text) && Enum.TryParse<T>(text, out var value) ? value : fallback;

	protected void Save<T>(string key, T value) where T : struct, Enum
	{
		Settings.Extra[key] = value.ToString();
		Settings.Save();
	}

	protected bool Flag(string key, bool fallback = false) => Settings.Extra.TryGetValue(key, out var on) ? on == "on" : fallback;

	protected void SetFlag(string key, bool on)
	{
		Settings.Extra[key] = on ? "on" : "off";
		Settings.Save();
	}

	/// <summary>Shows a speech bubble over a seat (null hides it).</summary>
	protected void Say(int seat, string? text)
	{
		var bubble = _bubbles[seat];
		if (bubble == null)
			return;
		bubble.Visible = text != null;
		bubble.Text = text ?? "";
	}

	protected void SayNone()
	{
		foreach (var bubble in _bubbles)
			if (bubble != null)
				bubble.Visible = false;
	}

	/// <summary>"You" or a computer player's name.</summary>
	protected static string Who(int seat) => seat == 0 ? "you" : Names[seat];

	/// <summary>Removes every card and puts the hands back, ready for a new deal.</summary>
	protected void ClearTable()
	{
		_fire?.Extinguish();
		_fire = null;
		SayNone();
		for (int s = 0; s < CardTable.MaxSeats; s++)
		{
			Hands[s]?.Reset();
			Fans[s]?.Clear();
		}
		foreach (var child in GetChildren())
			if (child is Card3D card)
				card.QueueFree();
		Hud.SetNewGameVisible(false);
		Hud.SetRestartVisible(false);
	}

	/// <summary>
	/// Lays a new deck face down in the middle of the table and shuffles it on screen (riffles
	/// and a bridge). Returns the card objects in the order given (index 0 is the top card).
	/// </summary>
	protected async Task<List<Card3D>> ShuffleOnTable(IReadOnlyList<Card> order, bool animate = true)
	{
		var stack = new List<Card3D>();
		for (int i = 0; i < order.Count; i++)
		{
			var node = new Card3D(order[order.Count - 1 - i], Looks);
			AddChild(node);
			stack.Add(node);
		}
		Dealer.Stack(stack, Table.Centre);
		if (animate)
		{
			Hud.SetStatus("Shuffling…", Muted);
			await Pause(0.3f);
			await Dealer.Riffle(stack, Table.Centre, times: 2);
		}
		// The shuffle moved the card objects around; give them the order wanted, top first.
		var top = new List<Card3D>(order.Count);
		for (int i = 0; i < order.Count; i++)
		{
			var node = stack[order.Count - 1 - i];
			node.Card = order[i];
			top.Add(node);
		}
		return top;
	}

	/// <summary>Deals cards from the top of the stack to the seats' piles, one at a time round the table.</summary>
	protected async Task DealRound(IReadOnlyList<Card3D> cards, IReadOnlyList<int> seats, float spacing = 0)
	{
		if (spacing <= 0)
			spacing = 0.6f / Math.Max(2, Players) + 0.04f;
		Hud.SetStatus("Dealing…", Muted);
		var onPile = new int[CardTable.MaxSeats];
		Tween? last = null;
		for (int j = 0; j < cards.Count; j++)
			last = Dealer.Deal(cards[j], Table.Pile[seats[j]], onPile[seats[j]]++, delay: j * spacing);
		await Await(last);
	}

	/// <summary>A player's working hand picks cards up from wherever they are and puts them into their fan.</summary>
	protected async Task IntoFan(int seat, IReadOnlyList<Card3D> cards)
	{
		if (cards.Count == 0)
			return;
		foreach (var node in cards)
		{
			foreach (var fan in Fans)
				fan?.Remove(node);
			Fans[seat].Add(node);
		}
		var targets = cards.Select(node => Fans[seat].Slot(Fans[seat].Cards.ToList().IndexOf(node), Fans[seat].Count)).ToList();
		await Hands[seat].Carry(cards, targets);
		await Await(Fans[seat].Settle());
	}

	/// <summary>Everyone picks up their dealt pile and fans it, all at once.</summary>
	protected async Task PickUpAll(Func<int, IEnumerable<Card3D>> pileOf)
	{
		var pickups = new List<Task>();
		for (int s = 0; s < Players; s++)
			pickups.Add(IntoFan(s, pileOf(s).OrderByDescending(n => n.GlobalPosition.Y).ToList()));
		await Task.WhenAll(pickups);
	}

	/// <summary>Takes a card out of a fan and plays it face up to a spot on the table, with a pinch.</summary>
	protected async Task PlayToTable(int seat, Card3D card, Transform3D target)
	{
		Fans[seat]?.Remove(card);
		Fans[seat]?.Settle();
		if (Hands[seat] != null)
			await Hands[seat].Carry(new[] { card }, new[] { target }, seconds: 0.36f, lift: 0.25f);
		else
			await Await(CardMotion.Fly(card, target, 0.36f, lift: 0.25f, landed: () => Sounds.PlaySlap(target.Origin)));
	}

	// ------------------------------------------------------------------ Trick-taking games

	/// <summary>Where a seat's card goes in the middle of the table when played to a trick.</summary>
	protected Vector3 TrickSpot(int seat)
	{
		var toward = Table.Pile[seat] - Table.Centre;
		toward.Y = 0;
		return Table.Centre + toward.Normalized() * 0.42f + Vector3.Up * (0.003f * (seat + 1));
	}

	/// <summary>Plays a card from a seat's fan into the middle, face up, turned towards its player.</summary>
	protected Task PlayToTrick(int seat, Card3D card)
	{
		var toward = Table.Pile[seat] - Table.Centre;
		float yaw = Mathf.Atan2(toward.X, toward.Z) + ((float)Random.NextDouble() - 0.5f) * 0.2f;
		return PlayToTable(seat, card, FaceUpAt(TrickSpot(seat), yaw));
	}

	/// <summary>
	/// The trick's winner's hand gathers its cards and puts them face down on their pile of won
	/// tricks (offset a little each trick, so the count shows). <paramref name="won"/> is how many
	/// tricks they had before this one.
	/// </summary>
	protected async Task SweepTrick(int winner, IReadOnlyList<Card3D> trick, int won)
	{
		var pile = Table.Winnings[winner] + new Vector3((won % 7) * 0.05f, 0, (won / 7) * 0.12f);
		var targets = trick.Select((_, i) => Dealer.InStack(pile, won * 4 + i, yaw: 0.25f * (won % 2))).ToList();
		await Hands[winner].Carry(trick.OrderByDescending(n => n.GlobalPosition.Y).ToList(), targets, seconds: 0.34f, lift: 0.18f);
	}

	/// <summary>Greys the cards in your fan you can't play now (or clears it, with null).</summary>
	protected void DimUnplayable(IEnumerable<Card>? playable)
	{
		var allowed = playable?.ToHashSet();
		foreach (var node in Fans[0].Cards)
			node.Dimmed = allowed != null && !allowed.Contains(node.Card);
	}

	/// <summary>Keeps your fan in trick-game order: by suit, high cards first.</summary>
	protected void ArrangeBySuit()
	{
		var bySuit = Tricks.Sorted(Fans[0].Cards.Select(n => n.Card));
		var lookup = Fans[0].Cards.ToDictionary(n => n.Card);
		Fans[0].Reorder(bySuit.Select(c => lookup[c]));
		Fans[0].Settle(0.18f);
	}

	/// <summary>A card lying face up on the felt at a point, turned a little.</summary>
	protected static Transform3D FaceUpAt(Vector3 at, float yaw = 0) => new(Card3D.FaceUp(yaw), at);

	/// <summary>A card lying face down on the felt at a point.</summary>
	protected static Transform3D FaceDownAt(Vector3 at, float yaw = 0) => new(Card3D.FaceDown(yaw), at);

	/// <summary>The card (of a set) under a tap: the nearest one hit.</summary>
	protected static Card3D? HitCard(IEnumerable<Card3D> cards, Vector3 origin, Vector3 direction)
	{
		Card3D? best = null;
		float bestDistance = float.MaxValue;
		foreach (var card in cards)
			if (card.Hit(origin, direction) is { } d && d < bestDistance - 0.0005f)
			{
				best = card;
				bestDistance = d;
			}
		return best;
	}

	/// <summary>
	/// Ends a round: the status line, the winner's cards set alight (if given), a win counted for
	/// you if you won, and Deal again offered (or, in the demo, the next round started).
	/// </summary>
	protected void Finish(string message, int winner, IEnumerable<Card3D>? winnersCards = null, Vector3? at = null)
	{
		Hud.SetStatus(message, Bright);
		if (winner >= 0)
		{
			Wins.Win(first: winner == 0);
			var meshes = (winnersCards ?? Enumerable.Empty<Card3D>()).SelectMany(n => n.Meshes).ToList();
			if (meshes.Count > 0)
				_fire = Flames.SetAlight(this, at ?? Table.Centre, meshes, radius: 0.5f, size: 1.2f, withLight: true);
		}
		Hud.SetRestartVisible(false);
		Hud.SetNewGameVisible(true);
		Busy = true;
		if (Demo)
			Run(DemoNext());
	}

	private async Task DemoNext()
	{
		await Pause(3f);
		if (QuitAfterDemo)
			GetTree().Quit();
		else
			OnNewGamePressed();
	}

	// ------------------------------------------------------------------ Input and the round's buttons

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Busy || Hud == null || Hud.MenuOpen || _styleMenu.Visible)
			return;
		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
			return;
		var (origin, direction) = Table.Ray(click.Position);
		Tapped(origin, direction, click.Position);
	}

	protected void OnNewGamePressed()
	{
		bool adDue = _adSchedule!.GameFinished();
		Settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		Settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			Ads.ShowInterstitial(() => Run(StartRound()));
		}
		else
		{
			Run(StartRound());
		}
	}

	/// <summary>Bumps the round count (so old animations stop), clears up, and starts the next round.</summary>
	protected async Task StartRound()
	{
		Round++;
		Busy = true;
		ClearTable();
		await NewRound();
	}

	/// <summary>Start over mid-game: against the computer it's a resignation unless an ad is watched.</summary>
	private void OnRestartPressed()
	{
		if (!InProgress)
		{
			Run(StartRound());
			return;
		}
		Hud.AskRestart(HasLevels && !Demo, countLoss =>
		{
			if (countLoss)
				Wins.Win(first: false);
			Run(StartRound());
		}, then => Ads.ShowRewarded(rewarded: then, closed: () => { }));
	}

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (the
	/// computer plays every seat), `--quit-after-demo`, `--players=4`, `--games-since-ad=4`,
	/// `--art=user://art` (paints every card design to PNGs and quits), `--hands` (the glass hands
	/// up close), plus the HUD's `--tutorial=N` and `--settings`, and the game's own.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					Demo = true;
					Settings.Muted = false;
					break;
				case "--quit-after-demo":
					QuitAfterDemo = true;
					break;
				case "--players":
					Players = Math.Clamp(int.Parse(value), MinPlayers, MaxPlayers);
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--art":
					_art = value;
					break;
				case "--hands":
					_handCheck = true;
					break;
				default:
					DevArg(name, value);
					break;
			}
		}
	}
}
