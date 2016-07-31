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

namespace Chatterino
{
    public class WinformsGuiEngine : IGuiEngine
    {
        // LINKS
        public void HandleLink(string link)
        {
            try
            {
                if (link.StartsWith("http://") || link.StartsWith("https://")
                    || MessageBox.Show($"The link \"{link}\" will be opened in an external application.", "open link", MessageBoxButtons.OKCancel) == DialogResult.OK)
                    Process.Start(link);
            }
            catch { }
        }


        // SOUNDS
        SoundPlayer snd = new SoundPlayer(Properties.Resources.ping2);
        public void PlaySound(NotificationSound sound)
        {
            try
            {
                bool focused = false;

                App.MainForm.Invoke(() => focused = App.MainForm.ContainsFocus);

                if (!focused)
                    snd.Play();
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

        public void HandleAnimatedTwitchEmote(TwitchEmote emote)
        {
            if (emote.Image != null)
            {
                Image img = (Image)emote.Image;

                bool animated = ImageAnimator.CanAnimate(img);

                if (animated)
                {
                    emote.Animated = true;

                    var dimension = new FrameDimension(img.FrameDimensionsList[0]);
                    var frameCount = img.GetFrameCount(dimension);
                    int[] frameDuration = new int[frameCount];
                    int currentFrame = 0;
                    int currentFrameOffset = 0;

                    byte[] times = img.GetPropertyItem(0x5100).Value;
                    int frame = 0;
                    for (int i = 0; i < frameCount; i++)
                    {
                        frameDuration[i] = BitConverter.ToInt32(times, 4 * frame);
                    }

                    App.GifEmoteFramesUpdating += (s, e) =>
                    {
                        currentFrameOffset += 3;

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

                        lock (img)
                            img.SelectActiveFrame(dimension, currentFrame);
                    };
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
                Image img = (Image)image;
                lock (img)
                    return new CommonSize(img.Width, img.Height);
            }
        }


        // MESSAGES
        bool enableBitmapDoubleBuffering = false;

        public CommonSize MeasureStringSize(object graphics, FontType font, string text)
        {
            Size size = TextRenderer.MeasureText((IDeviceContext)graphics, text, Fonts.GetFont(font), Size.Empty, App.DefaultTextFormatFlags);
            return new CommonSize(size.Width, size.Height);
        }

        public void DrawMessage(object graphics, Common.Message message, int xOffset2, int yOffset2, Selection selection, int currentLine)
        {
            try
            {
                message.X = xOffset2;
                message.Y = yOffset2;

                Graphics g2 = (Graphics)graphics;

                int spaceWidth = TextRenderer.MeasureText(g2, " ", Fonts.Medium, Size.Empty, App.DefaultTextFormatFlags).Width;

                int xOffset = 0, yOffset = 0;
                Graphics g = null;
                Bitmap bitmap = null;

                if (enableBitmapDoubleBuffering)
                {
                    if (message.buffer == null)
                    {
                        bitmap = new Bitmap(message.Width == 0 ? 10 : message.Width, message.Height == 0 ? 10 : message.Height);
                        g = Graphics.FromImage(bitmap);
                    }
                }
                else
                {
                    g = g2;
                    xOffset = xOffset2;
                    yOffset = yOffset2;
                }

                if (!enableBitmapDoubleBuffering || message.buffer == null)
                {
                    message.X = xOffset2;
                    var textColor = App.ColorScheme.Text;

                    if (message.Highlighted)
                        g.FillRectangle(App.ColorScheme.ChatBackgroundHighlighted, 0, yOffset, g.ClipBounds.Width, message.Height);

                    for (int i = 0; i < message.Words.Count; i++)
                    {
                        var word = message.Words[i];

                        if (word.Type == SpanType.Text)
                        {
                            Font font = Fonts.GetFont(word.Font);

                            Color color = word.Color == null ? textColor : Color.FromArgb(word.Color.Value);
                            if (word.Color != null && color.GetBrightness() < 0.5f)
                            {
                                color = ControlPaint.Light(color, 1f);
                            }

                            if (word.SplitSegments == null)
                            {
                                TextRenderer.DrawText(g, (string)word.Value, font, new Point(xOffset + word.X, word.Y + yOffset), color, App.DefaultTextFormatFlags);
                            }
                            else
                            {
                                var segments = word.SplitSegments;
                                for (int x = 0; x < segments.Length; x++)
                                {
                                    TextRenderer.DrawText(g, segments[x].Item1, font, new Point(xOffset + segments[x].Item2.X, yOffset + segments[x].Item2.Y), color, App.DefaultTextFormatFlags);
                                }
                            }
                        }
                        else if (word.Type == SpanType.Emote)
                        {
                            var emote = (TwitchEmote)word.Value;
                            var img = (Image)emote.Image;
                            if (img != null)
                            {
                                lock (img)
                                {
                                    g.DrawImage(img, word.X + xOffset, word.Y + yOffset, word.Width, word.Height);
                                }
                            }
                            else
                            {
                                g.DrawRectangle(Pens.Red, xOffset + word.X, word.Y + yOffset, word.Width, word.Height);
                            }
                        }
                        else if (word.Type == SpanType.Image)
                        {
                            var img = (Image)word.Value;
                            if (img != null)
                                g.DrawImage(img, word.X + xOffset, word.Y + yOffset, word.Width, word.Height);
                        }
                    }

                    if (message.Disabled)
                    {
                        Brush disabledBrush = new SolidBrush(Color.FromArgb(172, (App.ColorScheme.ChatBackground as SolidBrush)?.Color ?? Color.Black));
                        g.FillRectangle(disabledBrush, xOffset, yOffset, 1000, message.Height);
                    }

                    if (enableBitmapDoubleBuffering)
                    {
                        g.Flush();
                        message.buffer = bitmap;
                    }
                }

                if (enableBitmapDoubleBuffering)
                {
                    g2.DrawImageUnscaled((Image)message.buffer, xOffset2, yOffset2);
                    DrawGifEmotes(g2, message, selection, currentLine);
                }

                if (selection != null && !selection.IsEmpty && selection.First.MessageIndex <= currentLine && selection.Last.MessageIndex >= currentLine)
                {
                    for (int i = 0; i < message.Words.Count; i++)
                    {
                        if ((currentLine != selection.First.MessageIndex || i >= selection.First.WordIndex) && (currentLine != selection.Last.MessageIndex || i < selection.Last.WordIndex))
                        {
                            var word = message.Words[i];

                            if (!(word.Type == SpanType.Emote) || !((TwitchEmote)word.Value).Animated)
                                g2.FillRectangle(selectionBrush, word.X + xOffset2, word.Y + yOffset2, word.Width + spaceWidth, word.Height);
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                exc.Log("graphics");
            }
        }

        Brush selectionBrush = new SolidBrush(Color.FromArgb(127, Color.Orange));

        public void DrawGifEmotes(object graphics, Common.Message message, Selection selection, int currentLine)
        {
            var w = Stopwatch.StartNew();
            var Words = message.Words;
            Graphics g = (Graphics)graphics;

            int spaceWidth = TextRenderer.MeasureText(g, " ", Fonts.Medium, Size.Empty, App.DefaultTextFormatFlags).Width;

            for (int i = 0; i < Words.Count; i++)
            {
                var word = Words[i];

                TwitchEmote emote;
                if (word.Type == SpanType.Emote && (emote = (TwitchEmote)word.Value).Animated)
                {
                    if (emote.Image != null)
                    {
                        lock (emote.Image)
                        {
                            BufferedGraphicsContext context = BufferedGraphicsManager.Current;

                            var CurrentXOffset = message.X;
                            var CurrentYOffset = message.Y;

                            var buffer = context.Allocate(g, new Rectangle(word.X + CurrentXOffset, word.Y + CurrentYOffset, word.Width, word.Height));

                            buffer.Graphics.FillRectangle(message.Highlighted ? App.ColorScheme.ChatBackgroundHighlighted : App.ColorScheme.ChatBackground, word.X + CurrentXOffset, word.Y + CurrentYOffset, word.Width, word.Height);
                            buffer.Graphics.DrawImage((Image)emote.Image, word.X + CurrentXOffset, word.Y + CurrentYOffset, word.Width, word.Height);

                            //if (message.Highlighted)
                            //    g.FillRectangle(, word.X + CurrentXOffset, word.Y + CurrentYOffset, word.Width, word.Height);

                            if (selection != null && !selection.IsEmpty && (currentLine > selection.First.MessageIndex || (currentLine == selection.First.MessageIndex && i >= selection.First.WordIndex)) && (currentLine < selection.Last.MessageIndex || (selection.Last.MessageIndex == currentLine && i < selection.Last.WordIndex)))
                                buffer.Graphics.FillRectangle(selectionBrush, word.X + CurrentXOffset, word.Y + CurrentYOffset, word.Width, word.Height);

                            if (message.Disabled)
                            {
                                buffer.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(172, (App.ColorScheme.ChatBackground as SolidBrush)?.Color ?? Color.Black)),
                                    word.X + CurrentXOffset, word.Y + CurrentYOffset, word.Width + spaceWidth, word.Height);
                            }

                            buffer.Render(g);
                        }
                    }
                }
            }
            w.Stop();
            //Console.WriteLine($"Drew gif emotes in {w.Elapsed.TotalSeconds:0.000000} seconds");
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
    }
}
