using System;
using NUnit.Framework;
using UnityEditor;
using Wasteland.Audio;

namespace Wasteland.Audio.Tests
{
    public sealed class SoundBankTests
    {
        [Test]
        public void EveryCue_ResolvesToItsPlaceholderClip()
        {
            SoundBank bank = AssetDatabase.LoadAssetAtPath<SoundBank>(SoundBank.PlaceholderAssetPath);
            Assert.That(bank, Is.Not.Null);
            Assert.That(bank.EntryCount, Is.EqualTo(SoundCues.Count));
            Assert.That(bank.ContainsDuplicateCues(), Is.False);

            foreach (SoundCue cue in Enum.GetValues(typeof(SoundCue)))
            {
                bool found = bank.TryGet(cue, out SoundBankEntry entry);
                Assert.That(found, Is.True, cue.ToString());
                Assert.That(entry.Clip, Is.Not.Null, cue.ToString());
                Assert.That(AssetDatabase.GetAssetPath(entry.Clip), Is.EqualTo(SoundCues.ClipPath(cue)), cue.ToString());
                Assert.That(entry.Spatial, Is.EqualTo(ExpectSpatial(cue)), cue.ToString());
                Assert.That(entry.Volume, Is.EqualTo(1f), cue.ToString());
            }
        }

        [Test]
        public void OrderAck_HasARetriggerGap_AndUiClickDoesNot()
        {
            SoundBank bank = AssetDatabase.LoadAssetAtPath<SoundBank>(SoundBank.PlaceholderAssetPath);
            Assert.That(bank.TryGet(SoundCue.OrderAck, out SoundBankEntry ack), Is.True);
            Assert.That(ack.CooldownMilliseconds, Is.GreaterThan(0));

            Assert.That(bank.TryGet(SoundCue.UiClick, out SoundBankEntry click), Is.True);
            Assert.That(click.CooldownMilliseconds, Is.EqualTo(0));
        }

        [Test]
        public void FileNames_MatchThePlaceholderClips()
        {
            Assert.That(Enum.GetValues(typeof(SoundCue)).Length, Is.EqualTo(SoundCues.Count));
            Assert.That((int)SoundCue.UiClick, Is.EqualTo(0));
            Assert.That((int)SoundCue.OrderAck, Is.EqualTo(1));
            Assert.That((int)SoundCue.RaiseBattalion, Is.EqualTo(2));
            Assert.That((int)SoundCue.DepotBuilt, Is.EqualTo(3));
            Assert.That((int)SoundCue.CombatLoop, Is.EqualTo(4));
            Assert.That((int)SoundCue.TownCaptured, Is.EqualTo(5));
            Assert.That((int)SoundCue.TownLost, Is.EqualTo(6));
            Assert.That((int)SoundCue.WinStinger, Is.EqualTo(7));
            Assert.That((int)SoundCue.LoseStinger, Is.EqualTo(8));
            Assert.That((int)SoundCue.DrawStinger, Is.EqualTo(9));

            Assert.That(SoundCues.FileName(SoundCue.UiClick), Is.EqualTo("ui_click.wav"));
            Assert.That(SoundCues.FileName(SoundCue.OrderAck), Is.EqualTo("order_ack.wav"));
            Assert.That(SoundCues.FileName(SoundCue.RaiseBattalion), Is.EqualTo("raise_battalion.wav"));
            Assert.That(SoundCues.FileName(SoundCue.DepotBuilt), Is.EqualTo("depot_built.wav"));
            Assert.That(SoundCues.FileName(SoundCue.CombatLoop), Is.EqualTo("combat_loop.wav"));
            Assert.That(SoundCues.FileName(SoundCue.TownCaptured), Is.EqualTo("town_captured.wav"));
            Assert.That(SoundCues.FileName(SoundCue.TownLost), Is.EqualTo("town_lost.wav"));
            Assert.That(SoundCues.FileName(SoundCue.WinStinger), Is.EqualTo("win_stinger.wav"));
            Assert.That(SoundCues.FileName(SoundCue.LoseStinger), Is.EqualTo("lose_stinger.wav"));
            Assert.That(SoundCues.FileName(SoundCue.DrawStinger), Is.EqualTo("draw_stinger.wav"));
            Assert.That(SoundCues.FileName((SoundCue)99), Is.Null);
        }

        [Test]
        public void SpatialFlags_MatchTheSpec()
        {
            foreach (SoundCue cue in Enum.GetValues(typeof(SoundCue)))
            {
                Assert.That(SoundCues.IsSpatial(cue), Is.EqualTo(ExpectSpatial(cue)), cue.ToString());
            }
        }

        private static bool ExpectSpatial(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.RaiseBattalion:
                case SoundCue.DepotBuilt:
                case SoundCue.CombatLoop:
                case SoundCue.TownCaptured:
                case SoundCue.TownLost:
                    return true;
                default:
                    return false;
            }
        }
    }
}
