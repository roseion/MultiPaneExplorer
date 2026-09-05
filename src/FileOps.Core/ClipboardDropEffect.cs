namespace FileOps.Core;

/// <summary>资源管理器兼容的剪贴板拖放效果（"Preferred DropEffect" 流载荷）。</summary>
public enum ClipboardDropEffect
{
    None = 0,
    Copy = 1,
    Move = 2,
}

/// <summary>Preferred DropEffect 四字节载荷的编解码（与资源管理器剪贴板互通）。</summary>
public static class DropEffectCodec
{
    public const string ClipboardFormat = "Preferred DropEffect";

    public static byte[] Encode(ClipboardDropEffect effect) =>
        [(byte)((int)effect & 0xFF), 0, 0, 0];

    public static ClipboardDropEffect Decode(byte[]? payload)
    {
        if (payload is not { Length: >= 4 })
            return ClipboardDropEffect.None;
        return (ClipboardDropEffect)(payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24)) switch
        {
            ClipboardDropEffect.Copy => ClipboardDropEffect.Copy,
            ClipboardDropEffect.Move => ClipboardDropEffect.Move,
            _ => ClipboardDropEffect.None,
        };
    }
}
