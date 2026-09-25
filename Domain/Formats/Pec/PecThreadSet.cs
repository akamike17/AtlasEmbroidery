namespace AtlasEmbroidery.Domain.Formats.Pec;

using AtlasEmbroidery.Domain.Models;
using System.Collections.Generic;

/// <summary>
/// Brother PEC thread color set (built-in palette)
/// Based on pyembroidery's EmbThreadPec
/// </summary>
public static class PecThreadSet
{
    private static readonly ThreadColor[] _threads = new[]
    {
        new ThreadColor(0x00, 0x00, 0x00, "Black", "001", "Black"),
        new ThreadColor(0xFF, 0xFF, 0xFF, "White", "002", "White"),
        new ThreadColor(0xFF, 0x00, 0x00, "Red", "003", "Red"),
        new ThreadColor(0x00, 0xFF, 0x00, "Lime", "004", "Lime"),
        new ThreadColor(0x00, 0x00, 0xFF, "Blue", "005", "Blue"),
        new ThreadColor(0xFF, 0xFF, 0x00, "Yellow", "006", "Yellow"),
        new ThreadColor(0xFF, 0x00, 0xFF, "Fuchsia", "007", "Fuchsia"),
        new ThreadColor(0x00, 0xFF, 0xFF, "Aqua", "008", "Aqua"),
        new ThreadColor(0x80, 0x00, 0x00, "Maroon", "009", "Maroon"),
        new ThreadColor(0x00, 0x80, 0x00, "Green", "010", "Green"),
        new ThreadColor(0x00, 0x00, 0x80, "Navy", "011", "Navy"),
        new ThreadColor(0x80, 0x80, 0x00, "Olive", "012", "Olive"),
        new ThreadColor(0x80, 0x00, 0x80, "Purple", "013", "Purple"),
        new ThreadColor(0x00, 0x80, 0x80, "Teal", "014", "Teal"),
        new ThreadColor(0xC0, 0xC0, 0xC0, "Silver", "015", "Silver"),
        new ThreadColor(0x80, 0x80, 0x80, "Gray", "016", "Gray"),
        new ThreadColor(0xFF, 0x80, 0x80, "Light Red", "017", "Light Red"),
        new ThreadColor(0x80, 0xFF, 0x80, "Light Green", "018", "Light Green"),
        new ThreadColor(0x80, 0x80, 0xFF, "Light Blue", "019", "Light Blue"),
        new ThreadColor(0xFF, 0xFF, 0x80, "Light Yellow", "020", "Light Yellow"),
        new ThreadColor(0xFF, 0x80, 0xFF, "Light Fuchsia", "021", "Light Fuchsia"),
        new ThreadColor(0x80, 0xFF, 0xFF, "Light Aqua", "022", "Light Aqua"),
        new ThreadColor(0xFF, 0xA5, 0x00, "Orange", "023", "Orange"),
        new ThreadColor(0xA5, 0x2A, 0x2A, "Brown", "024", "Brown"),
        new ThreadColor(0xFF, 0xC0, 0xCB, "Pink", "025", "Pink"),
        new ThreadColor(0x8A, 0x2B, 0xE2, "Blue Violet", "026", "Blue Violet"),
        new ThreadColor(0x00, 0xFA, 0x9A, "Medium Spring Green", "027", "Medium Spring Green"),
        new ThreadColor(0xFF, 0xD7, 0x00, "Gold", "028", "Gold"),
        new ThreadColor(0xDA, 0x70, 0xD6, "Orchid", "029", "Orchid"),
        new ThreadColor(0x32, 0xCD, 0x32, "Lime Green", "030", "Lime Green"),
        new ThreadColor(0xFF, 0x69, 0xB4, "Hot Pink", "031", "Hot Pink"),
        new ThreadColor(0x41, 0x69, 0xE1, "Royal Blue", "032", "Royal Blue"),
        new ThreadColor(0x2E, 0x8B, 0x57, "Sea Green", "033", "Sea Green"),
        new ThreadColor(0xFF, 0x45, 0x00, "Orange Red", "034", "Orange Red"),
        new ThreadColor(0x9A, 0xCD, 0x32, "Yellow Green", "035", "Yellow Green"),
        new ThreadColor(0x00, 0xCE, 0xD1, "Dark Turquoise", "036", "Dark Turquoise"),
        new ThreadColor(0xFF, 0x14, 0x93, "Deep Pink", "037", "Deep Pink"),
        new ThreadColor(0x00, 0xBF, 0xFF, "Deep Sky Blue", "038", "Deep Sky Blue"),
        new ThreadColor(0x69, 0x69, 0x69, "Dim Gray", "039", "Dim Gray"),
        new ThreadColor(0x1E, 0x90, 0xFF, "Dodger Blue", "040", "Dodger Blue"),
        new ThreadColor(0xB2, 0x22, 0x22, "Firebrick", "041", "Firebrick"),
        new ThreadColor(0x22, 0x8B, 0x22, "Forest Green", "042", "Forest Green"),
        new ThreadColor(0xDC, 0x14, 0x3C, "Crimson", "043", "Crimson"),
        new ThreadColor(0xFF, 0x7F, 0x50, "Coral", "044", "Coral"),
        new ThreadColor(0x64, 0x95, 0xED, "Cornflower Blue", "045", "Cornflower Blue"),
        new ThreadColor(0xFF, 0xFA, 0xF0, "Floral White", "046", "Floral White"),
        new ThreadColor(0x22, 0x22, 0x22, "Very Dark Gray", "047", "Very Dark Gray"),
        new ThreadColor(0xFF, 0xE4, 0xE1, "Misty Rose", "048", "Misty Rose"),
        new ThreadColor(0xFF, 0xE4, 0xB5, "Moccasin", "049", "Moccasin"),
        new ThreadColor(0xFF, 0xDE, 0xAD, "Navajo White", "050", "Navajo White"),
        new ThreadColor(0xFF, 0xEB, 0xCD, "Blanched Almond", "051", "Blanched Almond"),
        new ThreadColor(0xFF, 0xF8, 0xDC, "Cornsilk", "052", "Cornsilk"),
        new ThreadColor(0xF5, 0xF5, 0xDC, "Beige", "053", "Beige"),
        new ThreadColor(0xF5, 0xDE, 0xB3, "Wheat", "054", "Wheat"),
        new ThreadColor(0xFA, 0xEB, 0xD7, "Antique White", "055", "Antique White"),
        new ThreadColor(0xFA, 0xF0, 0xE6, "Linen", "056", "Linen"),
        new ThreadColor(0xF0, 0xFF, 0xF0, "Honeydew", "057", "Honeydew"),
        new ThreadColor(0xF0, 0xF8, 0xFF, "Alice Blue", "058", "Alice Blue"),
        new ThreadColor(0xF5, 0xFF, 0xFA, "Mint Cream", "059", "Mint Cream"),
        new ThreadColor(0xF0, 0xFF, 0xFF, "Azure", "060", "Azure"),
        new ThreadColor(0xF5, 0xF5, 0xF5, "White Smoke", "061", "White Smoke"),
        new ThreadColor(0xFF, 0xF5, 0xEE, "Sea Shell", "062", "Sea Shell"),
        new ThreadColor(0xFF, 0xFA, 0xFA, "Snow", "063", "Snow"),
    };

    static PecThreadSet()
    {
        var list = new List<ThreadColor>(_threads);
        for (int i = list.Count; i < 256; i++)
        {
            byte gray = (byte)i;
            list.Add(new ThreadColor(gray, gray, gray, $"Gray {i}", $"{i:D3}", $"Gray {i}"));
        }
        _threads = list.ToArray();
    }

    public static IReadOnlyList<ThreadColor> GetThreadSet() => _threads;
}