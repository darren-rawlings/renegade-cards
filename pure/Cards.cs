using System;
using System.Collections.Generic;

namespace Renegade.Cards;

/// <summary>The four suits, in bridge order. Hearts and diamonds are red.</summary>
public enum Suit { Clubs, Diamonds, Hearts, Spades }

/// <summary>A playing card: rank 1 (ace) to 13 (king) and a suit.</summary>
public readonly record struct Card(int Rank, Suit Suit)
{
	public const int Ace = 1, Jack = 11, Queen = 12, King = 13;

	public bool IsRed => Suit is Suit.Hearts or Suit.Diamonds;

	public bool IsFace => Rank >= Jack;

	/// <summary>The corner index: A, 2 … 10, J, Q, K.</summary>
	public string Index => RankIndex(Rank);

	/// <summary>Position in a full deck (0–51), suit by suit, ace to king.</summary>
	public int Ordinal => (int)Suit * 13 + Rank - 1;

	public static Card FromOrdinal(int ordinal) => new(ordinal % 13 + 1, (Suit)(ordinal / 13));

	public static string RankIndex(int rank) => rank switch
	{
		Ace => "A",
		Jack => "J",
		Queen => "Q",
		King => "K",
		_ => rank.ToString(),
	};

	/// <summary>How the rank is said aloud: "Ace", "Seven", "Queen"; "Sevens", "Sixes" for more than one.</summary>
	public static string RankName(int rank, bool plural = false)
	{
		string name = rank switch
		{
			Ace => "Ace",
			2 => "Two",
			3 => "Three",
			4 => "Four",
			5 => "Five",
			6 => "Six",
			7 => "Seven",
			8 => "Eight",
			9 => "Nine",
			10 => "Ten",
			Jack => "Jack",
			Queen => "Queen",
			King => "King",
			_ => throw new ArgumentOutOfRangeException(nameof(rank)),
		};
		return plural ? (rank == 6 ? "Sixes" : name + "s") : name;
	}

	public override string ToString() => Index + Suit switch
	{
		Suit.Clubs => "♣",
		Suit.Diamonds => "♦",
		Suit.Hearts => "♥",
		_ => "♠",
	};
}

public static class Deck
{
	/// <summary>A new 52-card deck in order: clubs, diamonds, hearts, spades, each ace to king.</summary>
	public static List<Card> Standard()
	{
		var cards = new List<Card>(52);
		for (int i = 0; i < 52; i++)
			cards.Add(Card.FromOrdinal(i));
		return cards;
	}

	/// <summary>A fair (Fisher–Yates) shuffle, in place.</summary>
	public static void Shuffle<T>(IList<T> items, Random random)
	{
		for (int i = items.Count - 1; i > 0; i--)
		{
			int j = random.Next(i + 1);
			(items[i], items[j]) = (items[j], items[i]);
		}
	}

	/// <summary>A shuffled 52-card deck.</summary>
	public static List<Card> Shuffled(Random random)
	{
		var cards = Standard();
		Shuffle(cards, random);
		return cards;
	}
}
