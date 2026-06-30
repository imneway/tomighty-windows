//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Windows.Forms;

namespace Tomighty.Windows.Timer
{
    public interface IFullscreenWindowDetector
    {
        bool IsFullscreenWindowActiveOn(Screen screen, IntPtr ignoredWindowHandle);
    }
}
