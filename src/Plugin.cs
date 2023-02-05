using BepInEx;
using System;
using System.Linq;
using System.Security.Permissions;
using static CreatureTemplate.Relationship.Type;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace Fof;

[BepInPlugin("com.dual.fof", "FoF", "1.0.0")]
sealed class Plugin : BaseUnityPlugin
{
    private static Tracker.CreatureRepresentation MutualFriend(AbstractCreature self, AbstractCreature other)
    {
        if (self.abstractAI?.RealAI?.tracker is not Tracker tracker || other?.abstractAI?.RealAI?.tracker is not Tracker fofTracker || other.state.dead) {
            return null;
        }

        foreach (Tracker.CreatureRepresentation friendRep in tracker.creatures) {
            AbstractCreature friend = friendRep.representedCreature;

            // If `self` is friends with `friend`, and `other` is friends with `friend`, then it's mutual!
            if (friend != other && friend.state.alive
                && Friends(self, friendRep)
                && Friends(other, fofTracker.RepresentationForCreature(friend, addIfMissing: false))
                ) {

                return friendRep;
            }
        }
        return null;
    }

    private static bool Friends(AbstractCreature self, Tracker.CreatureRepresentation other)
    {
        if (other == null) {
            return false;
        }
        if (self.abstractAI.RealAI.friendTracker is FriendTracker f && f.friendRel?.subjectID == other.representedCreature.ID) {
            return true;
        }
        if (other.dynamicRelationship.currentRelationship.type == Pack) {
            return true;
        }
        if (self.realizedObject is Scavenger scav) {
            return other.representedCreature.realizedObject is Player && scav.AI.LikeOfPlayer(other.dynamicRelationship) > 0.35f;
        }
        return false;
    }

    public void OnEnable()
    {
        On.RelationshipTracker.DynamicRelationship.Update += DynamicRelationship_Update;
        On.LizardAI.DoIWantToBiteThisCreature += LizardAI_DoIWantToBiteThisCreature;
    }

    private void DynamicRelationship_Update(On.RelationshipTracker.DynamicRelationship.orig_Update orig, RelationshipTracker.DynamicRelationship self)
    {
        orig(self);

        if (UpdatedRelationship(self) is CreatureTemplate.Relationship fof) {
            if (fof.type != self.currentRelationship.type) {
                self.rt.SortCreatureIntoModule(self, fof);
            }
            self.trackerRep.priority = fof.intensity * self.trackedByModuleWeigth;
            self.currentRelationship = fof;
        }
    }

    private CreatureTemplate.Relationship? UpdatedRelationship(RelationshipTracker.DynamicRelationship rel)
    {
        // If already fine with the target, stay that way.
        if (rel.currentRelationship.type == Ignores || rel.currentRelationship.type == Pack || rel.currentRelationship.type == Uncomfortable) {
            return null;
        }

        AbstractCreature self = rel.rt.AI.creature;
        AbstractCreature fof = rel.trackerRep.representedCreature;

        if (MutualFriend(self, fof) is Tracker.CreatureRepresentation mutualFriend) {
            // Pack members of friends are pack members of ours.
            if (mutualFriend.dynamicRelationship.currentRelationship.type == Pack) {
                return new CreatureTemplate.Relationship(Pack, mutualFriend.dynamicRelationship.currentRelationship.intensity * 0.5f);
            }

            // Politely ignore them otherwise.
            return new CreatureTemplate.Relationship(Ignores, 0.5f);
        }

        return null;
    }

    private bool LizardAI_DoIWantToBiteThisCreature(On.LizardAI.orig_DoIWantToBiteThisCreature orig, LizardAI self, Tracker.CreatureRepresentation otherCrit)
    {
        if (!orig(self, otherCrit)) {
            return false;
        }
        // Would we bite our friend? If not, don't bite our friend's friends either.
        if (MutualFriend(self.creature, otherCrit.representedCreature) is Tracker.CreatureRepresentation friend && !orig(self, friend)) {
            return false;
        }
        return true;
    }
}
