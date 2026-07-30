using MediatR;
using System.Runtime.InteropServices;

namespace EveFPreview.Services.Interop
{
    // Definition for Window Placement Structure
    [StructLayout(LayoutKind.Sequential)]
    struct ANIMATIONINFO
    {
        public uint cbSize;
        public int iMinAnimate;
    }
}
