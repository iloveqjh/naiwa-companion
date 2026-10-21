// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Media;

namespace NaiwaCompanion
{
    // Keep the in-memory WAV alive for asynchronous SoundPlayer playback.
    internal sealed class ActionSound : IDisposable
    {
        private MemoryStream stream;
        private SoundPlayer player;
        internal bool Available { get { return player != null; } }
        internal ActionSound(byte[] wav)
        {
            if (wav == null) return;
            try
            {
                stream = new MemoryStream(wav, false);
                player = new SoundPlayer(stream); player.Load();
            }
            catch { Dispose(); throw; }
        }
        internal void Play() { if (player != null) player.Play(); }
        internal void Stop() { if (player != null) player.Stop(); }
        public void Dispose()
        {
            if (player != null) { player.Stop(); player.Dispose(); player = null; }
            if (stream != null) { stream.Dispose(); stream = null; }
        }
    }
}
