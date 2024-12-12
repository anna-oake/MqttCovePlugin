using Cove.Server.Chalk;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MqttCovePlugin;

public static class ChalkConverter {
	private static readonly PngEncoder Encoder = new() {
		BitDepth = PngBitDepth.Bit4,
		ColorType = PngColorType.Palette,
		CompressionLevel = PngCompressionLevel.BestCompression,
		FilterMethod = PngFilterMethod.Adaptive,
	};

	private static readonly Argb32[] Colours = [
		new Argb32(0xFF, 0xEE, 0xD5), // 0 - white
		new Argb32(0x05, 0x0B, 0x15), // 1 - black
		new Argb32(0xAC, 0x00, 0x29), // 2 - red
		new Argb32(0x00, 0x85, 0x83), // 3 - blue
		new Argb32(0xE6, 0x9D, 0x00), // 4 - yellow
		new Argb32(0xFF, 0x00, 0x00), // 5 - SPECIAL (we just do red)
		new Argb32(0x7D, 0xA2, 0x24), // 6 - green
	];

	private static Argb32 GetColour(int colour) {
		if (colour < 0 || colour >= Colours.Length) {
			return Colours[0];
		}
		return Colours[colour];
	}

	public static byte[] ToPNG(this ChalkCanvas c) {
		int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
		foreach (var point in c.chalkImage) {
			minX = Math.Min(minX, (int)point.Key.x);
			minY = Math.Min(minY, (int)point.Key.y);
			maxX = Math.Max(maxX, (int)point.Key.x);
			maxY = Math.Max(maxY, (int)point.Key.y);
		}
		var width = maxX - minX + 1;
		var height = maxY - minY + 1;

		Image<Argb32> image = new(width, height);
		image.ProcessPixelRows(acc => {
			foreach (var p in c.chalkImage) {
				if (p.Value < 0) {
					continue;
				}
				var x = (int)p.Key.x - minX;
				var y = (int)p.Key.y - minY;
				acc.GetRowSpan(y)[x] = GetColour(p.Value);
			}
		});

		// image.Mutate(x => x.Resize(width * 8, height * 8, KnownResamplers.NearestNeighbor));

		var stream = new MemoryStream();
		image.SaveAsPng(stream, Encoder);

		return stream.ToArray();
	}
}