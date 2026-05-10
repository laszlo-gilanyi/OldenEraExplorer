namespace AssetRipper.TextureDecoder.Rgb.Channels;

internal readonly struct Gray : IChannel
{
	static bool IChannel.IsRed => true;
	static bool IChannel.IsGreen => true;
	static bool IChannel.IsBlue => true;
	static bool IChannel.FullyUtilized => false;
}
