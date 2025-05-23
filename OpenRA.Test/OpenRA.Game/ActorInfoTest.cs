#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Traits;

namespace OpenRA.Test
{
	interface IMock : ITraitInfoInterface { }
	class MockTraitInfo : TraitInfo { public override object Create(ActorInitializer init) { return null; } }
	class MockInheritInfo : MockTraitInfo { }

	sealed class MockAInfo : MockInheritInfo, IMock { }
	sealed class MockBInfo : MockTraitInfo, Requires<IMock>, Requires<MockInheritInfo> { }
	sealed class MockCInfo : MockTraitInfo, Requires<MockBInfo> { }

	sealed class MockDInfo : MockTraitInfo, Requires<MockEInfo> { }
	sealed class MockEInfo : MockTraitInfo, Requires<MockFInfo> { }
	sealed class MockFInfo : MockTraitInfo, Requires<MockDInfo> { }

	sealed class MockGInfo : MockInheritInfo, IMock, NotBefore<MockAInfo> { }
	sealed class MockHInfo : MockTraitInfo, NotBefore<IMock>, NotBefore<MockInheritInfo>, NotBefore<MockBInfo> { }
	sealed class MockIInfo : MockTraitInfo, NotBefore<MockHInfo>, NotBefore<MockCInfo> { }

	sealed class MockJInfo : MockTraitInfo, NotBefore<MockKInfo> { }
	sealed class MockKInfo : MockTraitInfo, NotBefore<MockLInfo> { }
	sealed class MockLInfo : MockTraitInfo, NotBefore<MockJInfo> { }

	[TestFixture]
	public class ActorInfoTest
	{
		[TestCase(TestName = "Trait ordering sorts in dependency order correctly")]
		public void TraitOrderingSortsCorrectly()
		{
			var unorderedTraits = new TraitInfo[] { new MockBInfo(), new MockCInfo(), new MockAInfo(), new MockBInfo() };
			var actorInfo = new ActorInfo("test", unorderedTraits);
			var orderedTraits = actorInfo.TraitsInConstructOrder().ToArray();

			Assert.That(unorderedTraits, Is.EquivalentTo(orderedTraits));

			for (var i = 0; i < orderedTraits.Length; i++)
			{
				var traitTypesThatMustOccurBeforeThisTrait =
					ActorInfo.PrerequisitesOf(orderedTraits[i]).Concat(ActorInfo.OptionalPrerequisitesOf(orderedTraits[i]));
				var traitTypesThatOccurAfterThisTrait = orderedTraits.Skip(i + 1).Select(ti => ti.GetType());
				var traitTypesThatShouldOccurEarlier = traitTypesThatOccurAfterThisTrait.Intersect(traitTypesThatMustOccurBeforeThisTrait);
				Assert.That(traitTypesThatShouldOccurEarlier, Is.Empty, "Dependency order has not been satisfied.");
			}
		}

		[TestCase(TestName = "Trait ordering sorts in optional dependency order correctly")]
		public void OptionalTraitOrderingSortsCorrectly()
		{
			var unorderedTraits = new TraitInfo[] { new MockHInfo(), new MockIInfo(), new MockGInfo(), new MockHInfo() };
			var actorInfo = new ActorInfo("test", unorderedTraits);
			var orderedTraits = actorInfo.TraitsInConstructOrder().ToArray();

			Assert.That(unorderedTraits, Is.EquivalentTo(orderedTraits));

			for (var i = 0; i < orderedTraits.Length; i++)
			{
				var traitTypesThatMustOccurBeforeThisTrait =
					ActorInfo.PrerequisitesOf(orderedTraits[i]).Concat(ActorInfo.OptionalPrerequisitesOf(orderedTraits[i]));
				var traitTypesThatOccurAfterThisTrait = orderedTraits.Skip(i + 1).Select(ti => ti.GetType());
				var traitTypesThatShouldOccurEarlier = traitTypesThatOccurAfterThisTrait.Intersect(traitTypesThatMustOccurBeforeThisTrait);
				Assert.That(traitTypesThatShouldOccurEarlier, Is.Empty, "Dependency order has not been satisfied.");
			}
		}

		[TestCase(TestName = "Trait ordering exception reports missing dependencies")]
		public void TraitOrderingReportsMissingDependencies()
		{
			var actorInfo = new ActorInfo("test", new MockBInfo(), new MockCInfo());
			var ex = Assert.Throws<YamlException>(() => actorInfo.TraitsInConstructOrder());

			Assert.That(ex.Message, Does.Contain(nameof(MockBInfo)), "Exception message did not report a missing dependency.");
			Assert.That(ex.Message, Does.Contain(nameof(MockCInfo)), "Exception message did not report a missing dependency.");
			Assert.That(ex.Message, Does.Contain(nameof(MockInheritInfo)), "Exception message did not report a missing dependency (from a base class).");
			Assert.That(ex.Message, Does.Contain(nameof(IMock)), "Exception message did not report a missing dependency (from an interface).");
		}

		[TestCase(TestName = "Trait ordering allows optional dependencies to be missing")]
		public void TraitOrderingAllowsMissingOptionalDependencies()
		{
			var unorderedTraits = new TraitInfo[] { new MockHInfo(), new MockIInfo() };
			var actorInfo = new ActorInfo("test", unorderedTraits);
			var orderedTraits = actorInfo.TraitsInConstructOrder().ToArray();

			Assert.That(unorderedTraits, Is.EquivalentTo(orderedTraits));
		}

		[TestCase(TestName = "Trait ordering exception reports cyclic dependencies")]
		public void TraitOrderingReportsCyclicDependencies()
		{
			var actorInfo = new ActorInfo("test", new MockDInfo(), new MockEInfo(), new MockFInfo());
			var ex = Assert.Throws<YamlException>(() => actorInfo.TraitsInConstructOrder());

			Assert.That(ex.Message, Does.Contain(nameof(MockDInfo)), "Exception message should report all cyclic dependencies.");
			Assert.That(ex.Message, Does.Contain(nameof(MockEInfo)), "Exception message should report all cyclic dependencies.");
			Assert.That(ex.Message, Does.Contain(nameof(MockFInfo)), "Exception message should report all cyclic dependencies.");
		}

		[TestCase(TestName = "Trait ordering exception reports cyclic optional dependencies")]
		public void TraitOrderingReportsCyclicOptionalDependencies()
		{
			var actorInfo = new ActorInfo("test", new MockJInfo(), new MockKInfo(), new MockLInfo());
			var ex = Assert.Throws<YamlException>(() => actorInfo.TraitsInConstructOrder());

			Assert.That(ex.Message, Does.Contain(nameof(MockJInfo)), "Exception message should report all cyclic dependencies.");
			Assert.That(ex.Message, Does.Contain(nameof(MockKInfo)), "Exception message should report all cyclic dependencies.");
			Assert.That(ex.Message, Does.Contain(nameof(MockLInfo)), "Exception message should report all cyclic dependencies.");
		}
	}
}
