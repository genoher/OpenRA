--[[
   Copyright (c) The OpenRA Developers and Contributors
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

AtreidesMainBase = { AConYard1, AOutpost1, APalace, ARefinery1, ARefinery2, ARefinery3, AHeavyFactory1, ALightFactory1, AStarport, AHiTechFactory, AResearch, AGunt1, AGunt2, AGunt3, AGunt4, AGunt5, ARock1, ARock2, ARock3, ARock4, ABarracks1, ABarracks2, APower1, APower2, APower3, APower4, APower5, APower6, APower7, APower8, APower9, APower10, APower11, APower12, APower13, APower14 }
AtreidesSmall1Base = { AConYard2, ARefinery4, ABarracks3, AHeavyFactory2, ALightFactory2, ARepair, ARock5, ARock6, ARock7, ARock8, ARock9, APower15, APower16, APower17, APower18, APower19, APower20, APower21 }
AtreidesSmall2Base = { AOutpost2, ABarracks4, AGunt6, AGunt7, AGunt8, ARock10, APower22, APower23 }
CorrinoMainBase = { COutpost, CPalace, CRefinery1, CHeavyFactory1, CLightFactory1, CStarport, CResearch, CGunt1, CGunt2, CRock1, CRock2, CBarracks1, CPower1, CPower2, CPower3, CPower4, CPower5, CPower6, CPower7 }
CorrinoSmallBase = { CConYard, CRefinery2, CHeavyFactory2, CLightFactory2, CRock3, CRock4, CBarracks2, CPower8, CPower9, CPower10, CPower11 }

AtreidesReinforcements =
{
	easy =
	{
		{ "missile_tank", "trooper", "light_inf", "light_inf" },
		{ "quad", "light_inf", "combat_tank_a"},
		{ "light_inf", "trooper", "missile_tank" },
		{ "light_inf", "light_inf", "siege_tank" }
	},

	normal =
	{
		{ "missile_tank", "trooper", "trooper", "light_inf", "light_inf" },
		{ "quad", "trike", "combat_tank_a"},
		{ "trooper", "trooper", "missile_tank" },
		{ "light_inf", "light_inf", "light_inf", "siege_tank" },
		{ "combat_tank_a", "trike", "trike", "fremen" }
	},

	hard =
	{
		{ "missile_tank", "trooper", "trooper", "trooper", "light_inf", "light_inf" },
		{ "quad", "trike", "light_inf", "combat_tank_a"},
		{ "light_inf", "trooper", "trooper", "missile_tank" },
		{ "light_inf", "light_inf", "light_inf", "light_inf", "siege_tank" },
		{ "combat_tank_a", "trike", "trike", "fremen", "fremen" },
		{ "sonic_tank", "combat_tank_a", "combat_tank_a", "quad" }
	}
}

CorrinoStarportReinforcements =
{
	easy =
	{
		{ "sardaukar", "sardaukar", "missile_tank" },
		{ "trooper", "trooper", "siege_tank" },
		{ "sardaukar", "sardaukar", "sardaukar", "trooper", "trooper", "light_inf", "light_inf" }
	},

	normal =
	{
		{ "sardaukar", "sardaukar", "sardaukar", "missile_tank" },
		{ "trooper", "trooper", "trooper", "siege_tank" },
		{ "sardaukar", "sardaukar", "sardaukar", "trooper", "trooper", "trooper", "light_inf", "light_inf", "light_inf" }
	},

	hard =
	{
		{ "sardaukar", "sardaukar", "sardaukar", "sardaukar", "missile_tank" },
		{ "trooper", "trooper", "trooper", "trooper", "siege_tank" },
		{ "sardaukar", "sardaukar", "sardaukar", "trooper", "trooper", "trooper", "trooper", "light_inf", "light_inf", "light_inf", "light_inf" }
	}
}

AtreidesAttackDelay =
{
	easy = DateTime.Minutes(3) + DateTime.Seconds(30),
	normal = DateTime.Minutes(2) + DateTime.Seconds(30),
	hard = DateTime.Minutes(1) + DateTime.Seconds(30)
}

CorrinoStarportDelay =
{
	easy = DateTime.Minutes(10),
	normal = DateTime.Minutes(8),
	hard = DateTime.Minutes(6)
}

AtreidesAttackWaves =
{
	easy = 4,
	normal = 5,
	hard = 6
}

FremenGroupSize =
{
	easy = 2,
	normal = 4,
	hard = 6
}

InitialAtreidesReinforcements =
{
	{ "trooper", "trooper", "light_inf", "light_inf", "light_inf", "light_inf", "light_inf", "light_inf" },
	{ "trooper", "trooper", "trooper", "combat_tank_a", "combat_tank_a" },
	{ "combat_tank_a", "combat_tank_a", "quad", "quad", "trike" },
	{ "trooper", "trooper", "trooper", "trooper", "light_inf", "light_inf", "light_inf", "light_inf", "light_inf", "light_inf" },
	{ "trooper", "trooper", "trooper", "trooper", "trooper", "trooper", "combat_tank_a", "combat_tank_a" },
	{ "combat_tank_a", "quad", "quad", "trike", "trike", "trike" }
}

InitialCorrinoReinforcements = { "trooper", "trooper", "trooper", "trooper", "quad", "quad" }

AtreidesPaths =
{
	{ AtreidesEntry1.Location, AtreidesRally1.Location },
	{ AtreidesEntry2.Location, AtreidesRally2.Location },
	{ AtreidesEntry3.Location, AtreidesRally3.Location },
	{ AtreidesEntry4.Location, AtreidesRally4.Location }
}

InitialAtreidesPaths =
{
	{ AtreidesEntry5.Location, AtreidesRally5.Location },
	{ AtreidesEntry6.Location, AtreidesRally6.Location },
	{ AtreidesEntry7.Location, AtreidesRally7.Location },
	{ AtreidesEntry8.Location, AtreidesRally8.Location },
	{ AtreidesEntry9.Location, AtreidesRally9.Location },
	{ AtreidesEntry10.Location, AtreidesRally10.Location }
}

InitialCorrinoPaths =
{
	{ CorrinoEntry1.Location, CorrinoRally1.Location },
	{ CorrinoEntry2.Location, CorrinoRally2.Location }
}

HarkonnenReinforcements = { "combat_tank_h", "combat_tank_h", "siege_tank", "siege_tank", "missile_tank" }

HarkonnenPath = { HarkonnenEntry.Location, HarkonnenRally.Location }

SendStarportReinforcements = function()
	Trigger.AfterDelay(CorrinoStarportDelay[Difficulty], function()
		if CStarport.IsDead or CStarport.Owner ~= CorrinoMain then
			return
		end

		local reinforcements = Utils.Random(CorrinoStarportReinforcements[Difficulty])

		local units = Reinforcements.ReinforceWithTransport(CorrinoMain, "frigate", reinforcements, { CorrinoStarportEntry.Location, CStarport.Location + CVec.New(1, 1) }, { CorrinoStarportExit.Location })[2]
		Utils.Do(units, function(unit)
			unit.AttackMove(AtreidesAttackLocation)
			IdleHunt(unit)
		end)

		SendStarportReinforcements()
	end)
end

SendHarkonnenReinforcements = function(delay)
	Trigger.AfterDelay(delay, function()
		Reinforcements.ReinforceWithTransport(Harkonnen, "carryall.reinforce", HarkonnenReinforcements, HarkonnenPath, { HarkonnenPath[1] })
		Trigger.AfterDelay(DateTime.Seconds(5), function()
			Media.PlaySpeechNotification(Harkonnen, "Reinforce")
		end)
	end)
end

SendAirStrike = function()
	if AHiTechFactory.IsDead or AHiTechFactory.Owner ~= AtreidesMain then
		return
	end

	local targets = Utils.Where(Harkonnen.GetActors(), function(actor)
		return
			actor.HasProperty("Sell") and
			actor.Type ~= "wall" and
			actor.Type ~= "medium_gun_turret" and
			actor.Type ~= "large_gun_turret" and
			actor.Type ~= "silo" and
			actor.Type ~= "wind_trap"
	end)

	if #targets > 0 then
		AHiTechFactory.TargetAirstrike(Utils.Random(targets).CenterPosition)
	end

	Trigger.AfterDelay(DateTime.Minutes(5), SendAirStrike)
end

BuildFremen = function()
	if APalace.IsDead or APalace.Owner ~= AtreidesMain then
		return
	end

	APalace.Produce("fremen")
	APalace.Produce("fremen")

	Trigger.AfterDelay(DateTime.Seconds(5), function()
		IdleFremen = Utils.Where(AtreidesMain.GetActorsByType('fremen'), function(actor) return actor.IsIdle end)

		if #IdleFremen >= FremenGroupSize[Difficulty] then
			SendFremen()
		end
	end)

	Trigger.AfterDelay(DateTime.Minutes(1) + DateTime.Seconds (30), BuildFremen)
end

SendFremen = function()
	Utils.Do(IdleFremen, function(freman)
		freman.AttackMove(AtreidesAttackLocation)
		IdleHunt(freman)
	end)
end

Tick = function()
	if Harkonnen.HasNoRequiredUnits() then
		AtreidesMain.MarkCompletedObjective(KillHarkonnen1)
		AtreidesSmall1.MarkCompletedObjective(KillHarkonnen2)
		AtreidesSmall2.MarkCompletedObjective(KillHarkonnen3)
		CorrinoMain.MarkCompletedObjective(KillHarkonnen4)
		CorrinoSmall.MarkCompletedObjective(KillHarkonnen5)
	end

	if AtreidesMain.HasNoRequiredUnits() and AtreidesSmall1.HasNoRequiredUnits() and AtreidesSmall2.HasNoRequiredUnits() and not Harkonnen.IsObjectiveCompleted(KillAtreides) then
		Media.DisplayMessage(UserInterface.GetFluentMessage("atreides-annihilated"), Mentat)
		Harkonnen.MarkCompletedObjective(KillAtreides)
	end

	if CorrinoMain.HasNoRequiredUnits() and CorrinoSmall.HasNoRequiredUnits() and not Harkonnen.IsObjectiveCompleted(KillCorrino) then
		Media.DisplayMessage(UserInterface.GetFluentMessage("emperor-annihilated"), Mentat)
		Harkonnen.MarkCompletedObjective(KillCorrino)
	end

	if DateTime.GameTime % DateTime.Seconds(10) == 0 and LastHarvesterEaten[AtreidesMain] then
		local units = AtreidesMain.GetActorsByType("harvester")

		if #units > 0 then
			LastHarvesterEaten[AtreidesMain] = false
			ProtectHarvester(units[1], AtreidesMain, AttackGroupSize[Difficulty])
		end
	end

	if DateTime.GameTime % DateTime.Seconds(10) == 0 and LastHarvesterEaten[AtreidesSmall1] then
		local units = AtreidesSmall1.GetActorsByType("harvester")

		if #units > 0 then
			LastHarvesterEaten[AtreidesSmall1] = false
			ProtectHarvester(units[1], AtreidesSmall1, AttackGroupSize[Difficulty])
		end
	end

	if DateTime.GameTime % DateTime.Seconds(10) == 0 and LastHarvesterEaten[CorrinoMain] then
		local units = CorrinoMain.GetActorsByType("harvester")

		if #units > 0 then
			LastHarvesterEaten[CorrinoMain] = false
			ProtectHarvester(units[1], CorrinoMain, AttackGroupSize[Difficulty])
		end
	end

	if DateTime.GameTime % DateTime.Seconds(10) == 0 and LastHarvesterEaten[CorrinoSmall] then
		local units = CorrinoSmall.GetActorsByType("harvester")

		if #units > 0 then
			LastHarvesterEaten[CorrinoSmall] = false
			ProtectHarvester(units[1], CorrinoSmall, AttackGroupSize[Difficulty])
		end
	end
end

WorldLoaded = function()
	AtreidesMain = Player.GetPlayer("Atreides Main Base")
	AtreidesSmall1 = Player.GetPlayer("Atreides Small Base 1")
	AtreidesSmall2 = Player.GetPlayer("Atreides Small Base 2")
	CorrinoMain = Player.GetPlayer("Corrino Main Base")
	CorrinoSmall = Player.GetPlayer("Corrino Small Base")
	Harkonnen = Player.GetPlayer("Harkonnen")

	InitObjectives(Harkonnen)
	KillAtreides = AddPrimaryObjective(Harkonnen, "destroy-atreides")
	KillCorrino = AddPrimaryObjective(Harkonnen, "destroy-imperial-forces")
	KillHarkonnen1 = AddPrimaryObjective(AtreidesMain, "")
	KillHarkonnen2 = AddPrimaryObjective(AtreidesSmall1, "")
	KillHarkonnen3 = AddPrimaryObjective(AtreidesSmall2, "")
	KillHarkonnen4 = AddPrimaryObjective(CorrinoMain, "")
	KillHarkonnen5 = AddPrimaryObjective(CorrinoSmall, "")

	Camera.Position = HMCV.CenterPosition
	AtreidesAttackLocation = HarkonnenRally.Location

	Trigger.AfterDelay(DateTime.Minutes(5), SendAirStrike)
	Trigger.AfterDelay(DateTime.Minutes(1) + DateTime.Seconds (30), BuildFremen)

	Trigger.OnAllKilledOrCaptured(AtreidesMainBase, function()
		Utils.Do(AtreidesMain.GetGroundAttackers(), IdleHunt)
	end)

	Trigger.OnAllKilledOrCaptured(AtreidesSmall1Base, function()
		Utils.Do(AtreidesSmall1.GetGroundAttackers(), IdleHunt)
	end)

	Trigger.OnAllKilledOrCaptured(AtreidesSmall2Base, function()
		Utils.Do(AtreidesSmall2.GetGroundAttackers(), IdleHunt)
	end)

	Trigger.OnAllKilledOrCaptured(CorrinoMainBase, function()
		Utils.Do(CorrinoMain.GetGroundAttackers(), IdleHunt)
	end)

	Trigger.OnAllKilledOrCaptured(CorrinoSmallBase, function()
		Utils.Do(CorrinoSmall.GetGroundAttackers(), IdleHunt)
	end)

	local path = function() return Utils.Random(AtreidesPaths) end
	local waveCondition = function() return Harkonnen.IsObjectiveCompleted(KillAtreides) end
	local huntFunction = function(unit)
		unit.AttackMove(AtreidesAttackLocation)
		IdleHunt(unit)
	end
	SendCarryallReinforcements(AtreidesMain, 0, AtreidesAttackWaves[Difficulty], AtreidesAttackDelay[Difficulty], path, AtreidesReinforcements[Difficulty], waveCondition, huntFunction)

	SendStarportReinforcements()

	Actor.Create("upgrade.barracks", true, { Owner = AtreidesMain })
	Actor.Create("upgrade.light", true, { Owner = AtreidesMain })
	Actor.Create("upgrade.heavy", true, { Owner = AtreidesMain })
	Actor.Create("upgrade.hightech", true, { Owner = AtreidesMain })
	Actor.Create("upgrade.barracks", true, { Owner = AtreidesSmall1 })
	Actor.Create("upgrade.light", true, { Owner = AtreidesSmall1 })
	Actor.Create("upgrade.heavy", true, { Owner = AtreidesSmall1 })
	Actor.Create("upgrade.barracks", true, { Owner = AtreidesSmall2 })
	Actor.Create("upgrade.barracks", true, { Owner = CorrinoMain })
	Actor.Create("upgrade.light", true, { Owner = CorrinoMain })
	Actor.Create("upgrade.heavy", true, { Owner = CorrinoMain })
	Actor.Create("upgrade.barracks", true, { Owner = CorrinoSmall })
	Actor.Create("upgrade.light", true, { Owner = CorrinoSmall })
	Actor.Create("upgrade.heavy", true, { Owner = CorrinoSmall })
	Trigger.AfterDelay(0, ActivateAI)

	SendHarkonnenReinforcements(DateTime.Minutes(2) + DateTime.Seconds(30))
end
