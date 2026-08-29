using Bombd.Core;
using Bombd.Globals;
using Bombd.Serialization;

namespace Bombd.Types.Network.Objects;

public class VotePackage : INetworkWritable
{
    private const int MaxTrackSize = 4;
    private static readonly int[] VotableTracks =
    {
        0x00000264,
        0x000001FE,
        0x00000291,
        0x00000262,
        0x000001F5,
        0x000002D9,
        0x000002C1,
        0x00000387,
        0x00000393,
        0x00000246,
        0x00000359,
        0x000002E2,
        0x000003BF,
        0x00000208,
        0x0000033C,
        0x00000270,
        0x00000335,
        0x00000391,
        0x000002C4,
        0x00000304,
        0x0000021B,
        0x00000243,
        0x000002F7,
        0x00000276
    };
    
    private class VotableTrack
    {
        public int TrackId;
        public int Count;
        
        public void Set(int id)
        {
            TrackId = id;
            Count = 0;
        }
    }

    public bool IsVoting => _state == 1;
    public int NumPlayersVoted => _playerVotes.Count;
    
    // 0 = None
    // 1 = Voting
    // 2 = Completed?
    private int _state;
    private int _finalizedVotedTrack;
    private readonly VotableTrack[] _tracks = new VotableTrack[MaxTrackSize];
    private readonly Dictionary<int, VotableTrack> _playerVotes = new();
    
    public VotePackage()
    {
        for (int i = 0; i < MaxTrackSize; ++i)
            _tracks[i] = new VotableTrack();
    }

    public void Reset()
    {
        _state = 0;
        _finalizedVotedTrack = -1;
        foreach (VotableTrack track in _tracks) track.Set(0);
        _playerVotes.Clear();
    }
    
    public void RemoveVote(int userId)
    {
        if (!_playerVotes.TryGetValue(userId, out VotableTrack? votedTrack)) return;
        votedTrack.Count--;
        _playerVotes.Remove(userId);
    }
    
    public void CastVote(int userId, int trackId)
    {
        RemoveVote(userId);
        foreach (VotableTrack track in _tracks)
        {
            if (track.TrackId != trackId) continue;
            
            _playerVotes[userId] = track;
            track.Count++;
            break;
        }
    }
    
    public void StartVote(int replayCreationId, int playerCount)
    {
        _finalizedVotedTrack = -1;
        _state = 1;

        int trackIndex = 0;
        _tracks[trackIndex++].Set(replayCreationId);
        
        List<int> randomTracks = WebApiManager.GetRandomTracks(replayCreationId, playerCount);
        
        foreach (int track in randomTracks)
            _tracks[trackIndex++].Set(track);
    }
    
    public int FinishVote()
    {
        _state = 2;

        VotableTrack finalizedVotableTrack = _tracks[0];
        for (int i = 1; i < MaxTrackSize; ++i)
        {
            if (_tracks[i].Count > finalizedVotableTrack.Count)
                finalizedVotableTrack = _tracks[i];
        }

        _finalizedVotedTrack = finalizedVotableTrack.TrackId;

        return _finalizedVotedTrack;
    }
    
    public void Write(NetworkWriter writer)
    {
        writer.Write(_state);
        writer.Write(_finalizedVotedTrack);
        foreach (VotableTrack track in _tracks)
        {
            writer.Write(track.TrackId);
            writer.Write(track.Count);
        }
    }
}