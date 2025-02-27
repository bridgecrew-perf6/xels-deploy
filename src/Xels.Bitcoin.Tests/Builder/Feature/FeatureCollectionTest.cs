﻿using System;
using System.Threading.Tasks;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Builder.Feature;
using Xunit;

namespace Xels.Bitcoin.Tests.Builder.Feature
{
    public class FeatureCollectionTest
    {
        [Fact]
        public void AddToCollectionReturnsOfGivenType()
        {
            var collection = new FeatureCollection();

            collection.AddFeature<FeatureCollectionFullNodeFeature>();

            Assert.Single(collection.FeatureRegistrations);
            Assert.Equal(typeof(FeatureCollectionFullNodeFeature), collection.FeatureRegistrations[0].FeatureType);
        }

        [Fact]
        public void AddFeatureAlreadyInCollectionThrowsException()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var collection = new FeatureCollection();

                collection.AddFeature<FeatureCollectionFullNodeFeature>();
                collection.AddFeature<FeatureCollectionFullNodeFeature>();
            });
        }

        private class FeatureCollectionFullNodeFeature : IFullNodeFeature
        {
            /// <inheritdoc />
            public bool InitializeBeforeBase { get; set; }

            public FeatureInitializationState State { get; set; }

            public void LoadConfiguration()
            {
                throw new NotImplementedException();
            }

            public Task InitializeAsync()
            {
                throw new NotImplementedException();
            }

            public void Dispose()
            {
                throw new NotImplementedException();
            }

            public void ValidateDependencies(IFullNodeServiceProvider services)
            {
                throw new NotImplementedException();
            }

            public void WaitInitialized()
            {
            }

            public bool IsEnabled()
            {
                return true;
            }
        }
    }
}
