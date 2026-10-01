using System.Numerics;
using Dalamud.Bindings.ImGui;
using QRCoder;

namespace ChatTerror.Plugin.Gui;

public sealed class QrRenderer
{
    private const uint White = 0xFFFFFFFF;
    private const uint Black = 0xFF000000;

    private string? text;
    private bool[,]? modules;

    // QRCoder's matrix already includes the four-module quiet zone.
    public void Draw(string value, float moduleSize)
    {
        if (value != text || modules == null)
        {
            text = value;
            modules = Build(value);
        }

        var count = modules.GetLength(0);
        var origin = ImGui.GetCursorScreenPos();
        var size = count * moduleSize;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(origin, origin + new Vector2(size, size), White);

        for (var y = 0; y < count; y++)
        {
            for (var x = 0; x < count; x++)
            {
                if (!modules[y, x])
                    continue;
                var min = origin + new Vector2(x * moduleSize, y * moduleSize);
                drawList.AddRectFilled(min, min + new Vector2(moduleSize, moduleSize), Black);
            }
        }

        ImGui.Dummy(new Vector2(size, size));
    }

    private static bool[,] Build(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var result = new bool[matrix.Count, matrix.Count];
        for (var y = 0; y < matrix.Count; y++)
        {
            for (var x = 0; x < matrix.Count; x++)
                result[y, x] = matrix[y][x];
        }

        return result;
    }
}
