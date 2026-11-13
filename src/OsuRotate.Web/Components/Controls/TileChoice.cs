namespace OsuRotate.Web.Components.Controls;

// One tile in a TilePicker: the value it stands for, its name, and an optional short note.
public sealed record TileChoice<TValue>(TValue Value, string Name, string? Note = null);
