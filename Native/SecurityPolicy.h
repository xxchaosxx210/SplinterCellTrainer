#pragma once
namespace SecurityPolicy {
inline bool Target(bool camera,bool turret,unsigned group){return group==512?camera:group==1024||group==2048?turret:false;}
inline bool RestoreState(bool sameActor,bool stillDisabled,bool destroyed){return sameActor&&stillDisabled&&!destroyed;}
inline bool RestoreIff(bool sameActor,unsigned current,unsigned applied){return sameActor&&current==applied;}
}
