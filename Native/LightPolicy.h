#pragma once
namespace LightPolicy {
inline bool ShouldSuppress(bool supportedLight,bool sameLevel,bool ownedByInventoryOrPlayer,bool deleted){return supportedLight&&sameLevel&&!ownedByInventoryOrPlayer&&!deleted;}
inline bool ShouldRestore(bool sameIdentity,bool destroyed){return sameIdentity&&!destroyed;}
inline bool StillMuted(unsigned current){return current==0;}
}
