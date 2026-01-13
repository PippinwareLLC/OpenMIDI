using OpenMIDI.Playback;

namespace OpenMIDI.Tests;

public sealed class MidiChannelNoteTrackerTests
{
    [Fact]
    public void Tracker_MaintainsActiveCountsPerNote()
    {
        MidiChannelNoteTracker tracker = new MidiChannelNoteTracker();

        tracker.NoteOn(0, 60, 100);
        tracker.NoteOn(0, 60, 100);

        Assert.True(tracker.IsActive(0, 60));
        Assert.Equal(1, tracker.GetActiveNoteCount(0));

        tracker.NoteOff(0, 60, 0);
        Assert.True(tracker.IsActive(0, 60));
        Assert.Equal(1, tracker.GetActiveNoteCount(0));

        tracker.NoteOff(0, 60, 0);
        Assert.False(tracker.IsActive(0, 60));
        Assert.Equal(0, tracker.GetActiveNoteCount(0));
    }

    [Fact]
    public void Tracker_NoteOnVelocityZeroActsLikeNoteOff()
    {
        MidiChannelNoteTracker tracker = new MidiChannelNoteTracker();

        tracker.NoteOn(0, 60, 100);
        tracker.NoteOn(0, 60, 0);

        Assert.False(tracker.IsActive(0, 60));
        Assert.Equal(0, tracker.GetActiveNoteCount(0));
    }
}
