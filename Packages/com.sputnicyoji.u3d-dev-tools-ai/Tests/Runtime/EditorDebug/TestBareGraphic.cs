using UnityEngine.UI;

namespace Yoji.EditorDebug.Tests
{
    /// 仿世界空间 TMPro.TextMeshPro：是 Graphic 却没有 CanvasRenderer（Graphic 不 RequireComponent 它）。
    public sealed class TestBareGraphic : Graphic
    {
    }
}
