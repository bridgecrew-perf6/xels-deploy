﻿using System.Collections.Generic;
using System.IO;
using NBitcoin;
using Xels.Bitcoin.Features.PoA.Voting;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Features.PoA.Tests
{
    public class PollTests
    {
        [Fact]
        public void CanSerializeAndDeserialize()
        {
            Poll poll = new Poll()
            {
                Id = 5,
                VotingData = new VotingData()
                {
                    Data = RandomUtils.GetBytes(50),
                    Key = VoteKey.AddFederationMember
                },
                PollVotedInFavorBlockData = new HashHeightPair(uint256.One, 1),
                PollStartBlockData = new HashHeightPair(uint256.One, 1),
                PubKeysHexVotedInFavor = new List<Vote>()
                {
                    new Vote() { PubKey = "qwe" },
                    new Vote() { PubKey = "rty" }
                }
            };

            byte[] serializedBytes;

            using (var memoryStream = new MemoryStream())
            {
                var serializeStream = new BitcoinStream(memoryStream, true);

                serializeStream.ReadWrite(ref poll);

                serializedBytes = memoryStream.ToArray();
            }

            var deserializedPoll = new Poll();

            using (var memoryStream = new MemoryStream(serializedBytes))
            {
                var deserializeStream = new BitcoinStream(memoryStream, false);

                deserializeStream.ReadWrite(ref deserializedPoll);
            }

            Assert.Equal(poll.Id, deserializedPoll.Id);
            Assert.Equal(poll.VotingData, deserializedPoll.VotingData);
            Assert.Equal(poll.PollVotedInFavorBlockData, deserializedPoll.PollVotedInFavorBlockData);
            Assert.Equal(poll.PollStartBlockData, deserializedPoll.PollStartBlockData);
            Assert.Equal(poll.PubKeysHexVotedInFavor.Count, deserializedPoll.PubKeysHexVotedInFavor.Count);

            for (int i = 0; i < poll.PubKeysHexVotedInFavor.Count; i++)
            {
                Assert.Equal(poll.PubKeysHexVotedInFavor[i].PubKey, deserializedPoll.PubKeysHexVotedInFavor[i].PubKey);
                Assert.Equal(poll.PubKeysHexVotedInFavor[i].Height, deserializedPoll.PubKeysHexVotedInFavor[i].Height);
            }
        }
    }
}
