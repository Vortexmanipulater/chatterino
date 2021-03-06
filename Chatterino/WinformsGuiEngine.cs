﻿using Chatterino.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Drawing;
using System.Media;
using System.Drawing.Imaging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Chatterino.Controls;

namespace Chatterino
{
    public class WinformsGuiEngine : IGuiEngine
    {
        public WinformsGuiEngine()
        {
            AppSettings.FontChanged += (s, e) =>
            {
                gdiSizeCaches.Clear();
                dwSizeCaches.Clear();
            };
        }

        // LINKS
        public void HandleLink(Link _link)
        {
            switch (_link.Type)
            {
                case LinkType.Url:
                    {
                        var link = _link.Value as string;
                        try
                        {
                            if (link.StartsWith("http://") || link.StartsWith("https://")
                                || MessageBox.Show($"The link \"{link}\" will be opened in an external application.", "open link", MessageBoxButtons.OKCancel) == DialogResult.OK)
                                Process.Start(link);
                        }
                        catch { }
                    }
                    break;
                case LinkType.CloseCurrentSplit:
                    App.MainForm.RemoveSelectedSplit();
                    break;
                case LinkType.InsertText:
                    (App.MainForm.Selected as ChatControl)?.Input.Logic.InsertText(_link.Value as string);
                    break;
                case LinkType.UserInfo:
                    {
                        var data = (UserInfoData)_link.Value;

                        var popup = new UserInfoPopup(data)
                        {
                            StartPosition = FormStartPosition.Manual,
                            Location = Cursor.Position
                        };

                        popup.Show();

                        var screen = Screen.FromPoint(Cursor.Position);

                        int x = popup.Location.X, y = popup.Location.Y;

                        if (popup.Location.X < screen.WorkingArea.X)
                        {
                            x = screen.WorkingArea.X;
                        }
                        else if (popup.Location.X + popup.Width > screen.WorkingArea.Right)
                        {
                            x = screen.WorkingArea.Right - popup.Width;
                        }

                        if (popup.Location.Y < screen.WorkingArea.Y)
                        {
                            y = screen.WorkingArea.Y;
                        }
                        else if (popup.Location.Y + popup.Height > screen.WorkingArea.Bottom)
                        {
                            y = screen.WorkingArea.Bottom - popup.Height;
                        }

                        popup.Location = new Point(x, y);
                    }
                    break;
                case LinkType.ShowChannel:
                    {
                        var channelName = (string)_link.Value;

                        var widget = App.MainForm.TabControl.TabPages
                            .Where(x => x is ColumnTabPage)
                            .SelectMany(x => ((ColumnTabPage)x).Columns.SelectMany(y => y.Widgets))
                            .FirstOrDefault(
                                c => c is ChatControl && string.Equals(((ChatControl)c).ChannelName, channelName));

                        if (widget != null)
                        {
                            App.MainForm.TabControl.Select(widget.Parent as Controls.TabPage);
                            widget.Select();
                        }
                    }
                    break;
            }
        }


        // SOUNDS
        SoundPlayer defaultHighlightSound = new SoundPlayer(Properties.Resources.ping2);
        public SoundPlayer HighlightSound { get; private set; } = null;

        public bool IsDarkTheme
        {
            get
            {
                return !App.ColorScheme.IsLightTheme;
            }
        }

        DateTime highlightTimeStamp = DateTime.MinValue;

        public void PlaySound(NotificationSound sound, bool forceCustom = false)
        {
            try
            {
                var focused = false;

                App.MainForm.Invoke(() => focused = App.MainForm.ContainsFocus);

                if (!focused)
                {
                    SoundPlayer player = null;

                    if (forceCustom || AppSettings.ChatCustomHighlightSound)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(Path.Combine(Util.GetUserDataPath(), "Custom", "Ping.wav"));
                            if (fileInfo.Exists)
                            {
                                if (fileInfo.LastWriteTime != highlightTimeStamp)
                                {
                                    HighlightSound?.Dispose();

                                    try
                                    {
                                        using (var stream = new FileStream(Path.Combine(Util.GetUserDataPath(), "Custom", "Ping.wav"), FileMode.Open))
                                        {
                                            HighlightSound = new SoundPlayer(stream);
                                            HighlightSound.Load();
                                        }

                                        player = HighlightSound;
                                        Console.WriteLine("loaded");
                                    }
                                    catch
                                    {
                                        HighlightSound.Dispose();
                                    }
                                }
                            }
                            else
                            {
                                player = HighlightSound;
                            }
                        }
                        catch { }
                    }

                    if (player == null)
                    {
                        player = defaultHighlightSound;
                    }

                    player.Play();
                }
            }
            catch { }
        }


        // IMAGES
        public object ReadImageFromStream(Stream stream)
        {
            try
            {
                return Image.FromStream(stream);
            }
            catch { }

            return null;
        }

        public void HandleAnimatedTwitchEmote(LazyLoadedImage emote)
        {
            if (emote.Image != null)
            {
                var img = (Image)emote.Image;

                var animated = ImageAnimator.CanAnimate(img);

                if (animated)
                {
                    try
                    {
                        var dimension = new FrameDimension(img.FrameDimensionsList[0]);
                        var frameCount = img.GetFrameCount(dimension);
                        var frameDuration = new int[frameCount];
                        var currentFrame = 0;
                        var currentFrameOffset = 0;

                        var times = img.GetPropertyItem(0x5100).Value;
                        var frame = 0;
                        for (var i = 0; i < frameCount; i++)
                        {
                            var num = BitConverter.ToInt32(times, 4 * frame);

                            if (num == 0)
                            {
                                frameDuration[i] = 4;
                            }
                            else
                            {
                                frameDuration[i] = num;
                            }
                        }
                        emote.IsAnimated = true;

                        Console.WriteLine("new gif emote " + emote.Name);

                        App.GifEmoteFramesUpdating += (s, e) =>
                        {
                            currentFrameOffset += 3;

                            var oldCurrentFrame = currentFrame;

                            while (true)
                            {
                                if (currentFrameOffset > frameDuration[currentFrame])
                                {
                                    currentFrameOffset -= frameDuration[currentFrame];
                                    currentFrame = (currentFrame + 1) % frameCount;
                                }
                                else
                                    break;
                            }

                            if (oldCurrentFrame != currentFrame)
                            {
                                lock (img)
                                {
                                    img.SelectActiveFrame(dimension, currentFrame);
                                }
                            }
                        };
                    }
                    catch { }
                }
                App.TriggerEmoteLoaded();
            }
        }

        Dictionary<ImageType, Image> images = new Dictionary<ImageType, Image>
        {
            [ImageType.BadgeAdmin] = Properties.Resources.admin_bg,
            [ImageType.BadgeBroadcaster] = Properties.Resources.broadcaster_bg,
            [ImageType.BadgeDev] = Properties.Resources.dev_bg,
            [ImageType.BadgeGlobalmod] = Properties.Resources.globalmod_bg,
            [ImageType.BadgeModerator] = Properties.Resources.moderator_bg,
            [ImageType.BadgeStaff] = Properties.Resources.staff_bg,
            [ImageType.BadgeTurbo] = Properties.Resources.turbo_bg,
            [ImageType.BadgeTwitchPrime] = Properties.Resources.twitchprime_bg,

            [ImageType.Cheer1] = Properties.Resources.cheer1,
            [ImageType.Cheer100] = Properties.Resources.cheer100,
            [ImageType.Cheer1000] = Properties.Resources.cheer1000,
            [ImageType.Cheer5000] = Properties.Resources.cheer5000,
            [ImageType.Cheer10000] = Properties.Resources.cheer10000,
            [ImageType.Cheer100000] = Properties.Resources.cheer100000,
        };

        public object GetImage(ImageType type)
        {
            lock (images)
            {
                Image img;
                return images.TryGetValue(type, out img) ? img : null;
            }
        }

        public CommonSize GetImageSize(object image)
        {
            if (image == null)
            {
                return new CommonSize();
            }
            else
            {
                try
                {
                    var img = (Image)image;
                    lock (img)
                        return new CommonSize(img.Width, img.Height);
                }
                catch { }

                return new CommonSize(16, 16);
            }
        }


        // MESSAGES
        static int sizeCacheStackLimit = 2048;

        ConcurrentDictionary<FontType, Tuple<ConcurrentDictionary<string, CommonSize>, ConcurrentStack<string>, int>> gdiSizeCaches = new ConcurrentDictionary<FontType, Tuple<ConcurrentDictionary<string, CommonSize>, ConcurrentStack<string>, int>>();
        ConcurrentDictionary<FontType, Tuple<ConcurrentDictionary<string, CommonSize>, ConcurrentStack<string>, int>> dwSizeCaches = new ConcurrentDictionary<FontType, Tuple<ConcurrentDictionary<string, CommonSize>, ConcurrentStack<string>, int>>();

        public CommonSize MeasureStringSize(object graphics, FontType font, string text)
        {
            var isGdi = graphics is Graphics;

            var sizeCache = (isGdi ? gdiSizeCaches : dwSizeCaches).GetOrAdd(font, f =>
            {
                int lineHeight;

                if (isGdi)
                {
                    lineHeight = TextRenderer.MeasureText((Graphics)graphics, "X", Fonts.GetFont(font), Size.Empty, App.DefaultTextFormatFlags).Height;
                }
                else
                {
                    using (var layout = new SharpDX.DirectWrite.TextLayout(Fonts.Factory, "X", Fonts.GetTextFormat(font), 1000000, 1000000))
                    {
                        var metrics = layout.Metrics;
                        lineHeight = (int)metrics.Height;
                        //lineHeight = (int)Math.Ceiling(Fonts.GetTextFormat(font).FontSize);
                    }
                }

                return Tuple.Create(new ConcurrentDictionary<string, CommonSize>(), new ConcurrentStack<string>(), lineHeight);
            });

            return sizeCache.Item1.GetOrAdd(text, s =>
            {
                if (sizeCache.Item2.Count >= sizeCacheStackLimit)
                {
                    string value;
                    if (sizeCache.Item2.TryPop(out value))
                    {
                        CommonSize _s;
                        sizeCache.Item1.TryRemove(value, out _s);
                    }
                }

                sizeCache.Item2.Push(s);

                if (isGdi)
                {
                    var size = TextRenderer.MeasureText((IDeviceContext)graphics, text, Fonts.GetFont(font), Size.Empty, App.DefaultTextFormatFlags);
                    return new CommonSize(size.Width, sizeCache.Item3);
                }
                else
                {
                    using (var layout = new SharpDX.DirectWrite.TextLayout(Fonts.Factory, text, Fonts.GetTextFormat(font), 1000000, 1000000))
                    {
                        var metrics = layout.Metrics;

                        return new CommonSize((int)metrics.WidthIncludingTrailingWhitespace, sizeCache.Item3);
                    }
                }
            });
        }

        public void DisposeMessageGraphicsBuffer(Common.Message message)
        {
            if (message.buffer != null)
            {
                ((IDisposable)message.buffer).Dispose();
                message.buffer = null;
            }
        }

        public void FlashTaskbar()
        {
            if (!Util.IsLinux && App.MainForm != null)
            {

                App.MainForm.Invoke(() => Win32.FlashWindow.Flash(App.MainForm, 1));

                //App.MainForm.Invoke(() => Win32.FlashWindowEx(App.MainForm));
            }
        }

        public object ScaleImage(object image, double scale)
        {
            var img = (Image)image;

            int w = (int)(img.Width * scale), h = (int)(img.Height * scale);

            var newImage = new Bitmap(w, h);

            using (var graphics = Graphics.FromImage(newImage))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
                graphics.DrawImage(img, 0, 0, w, h);
            }

            return newImage;
        }

        public void FreezeImage(object img)
        {

        }

        public object DrawImageBackground(object image, HSLColor color)
        {
            var img = (Image)image;

            var bitmap = new Bitmap(img.Width, img.Height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(color.ToColor());
                g.DrawImage(img, 0, 0);
            }

            return bitmap;
        }

        public void ExecuteHotkeyAction(HotkeyAction action)
        {

        }
    }
}
