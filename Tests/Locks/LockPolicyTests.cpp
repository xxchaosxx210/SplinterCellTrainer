#include "../../Native/LockPolicy.h"
#include <cstdio>
#include <limits>
static bool Check(bool condition,const char* label){if(!condition){std::fprintf(stderr,"FAIL %s\n",label);return false;}std::printf("PASS %s\n",label);return true;}
int main(){
    bool ok=true;
    ok&=Check(LockPolicy::PrepareDoor(true,true,true,false,10000),"nearby closed ordinary lock can be prepared");
    ok&=Check(!LockPolicy::PrepareDoor(false,true,true,false,10000),"script-blocked unusable door stays blocked");
    ok&=Check(!LockPolicy::PrepareDoor(true,true,true,true,10000),"keypad and retinal opener doors are not directly unlocked");
    ok&=Check(!LockPolicy::PrepareDoor(true,false,true,false,10000),"already unlocked door is not tracked or rewritten");
    ok&=Check(!LockPolicy::PrepareDoor(true,true,false,false,10000),"open door is not prepared");
    ok&=Check(!LockPolicy::PrepareDoor(true,true,true,false,LockPolicy::RadiusSquared+1),"distant ordinary door is left untouched");
    ok&=Check(!LockPolicy::PrepareDoor(true,true,true,false,std::numeric_limits<float>::quiet_NaN()),"invalid position is rejected");
    ok&=Check(LockPolicy::RestoreDoor(true,false),"unused closed lock is restored when bypass ends");
    ok&=Check(!LockPolicy::RestoreDoor(false,false)&&!LockPolicy::RestoreDoor(true,true),"opening or committed interaction is not relocked");
    ok&=Check(LockPolicy::GrantKeypad(true,true,true),"Sam's active keypad can follow normal access granted path");
    ok&=Check(!LockPolicy::GrantKeypad(true,false,true)&&!LockPolicy::GrantKeypad(false,true,true)&&!LockPolicy::GrantKeypad(true,true,false),"NPC, idle and unrelated keypad interactions are excluded");
    return ok?0:1;
}
