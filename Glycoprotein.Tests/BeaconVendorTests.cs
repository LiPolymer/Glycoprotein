using Glycoprotein.Glycosylation;
using Xunit;

namespace Glycoprotein.Tests;

public class BeaconVendorTests {
    [Fact]
    public void Beacon_WithVendor_RoundTrips() {
        Glycosyl.Beacon beacon = new Glycosyl.Beacon {
            Id = "node-a",
            Vendor = "GlycoLinker v1.0.0",
            Fields = []
        };

        Glycosyl round = Glycosyl.FromBytes(beacon.ToBytes());
        Glycosyl.Beacon restored = Assert.IsType<Glycosyl.Beacon>(round);

        Assert.Equal("node-a",restored.Id);
        Assert.Equal("GlycoLinker v1.0.0",restored.Vendor);
    }

    [Fact]
    public void Beacon_WithoutVendor_DeserializesAsNull() {
        Glycosyl.Beacon beacon = new Glycosyl.Beacon {
            Id = "node-b",
            Fields = []
        };

        Glycosyl round = Glycosyl.FromBytes(beacon.ToBytes());
        Glycosyl.Beacon restored = Assert.IsType<Glycosyl.Beacon>(round);

        Assert.Null(restored.Vendor);
    }
}
