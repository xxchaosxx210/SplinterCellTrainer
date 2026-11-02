#include "../../Native/FightPolicy.h"
#include <cstdio>
static bool Check(bool b,const char* n){std::printf("%s %s\n",b?"PASS":"FAIL",n);return b;}
int main(){bool ok=true;using namespace FightPolicy;
 ok&=Check(Eligible(true,true,true,true,true,true,false,100),"armed conscious hostile guard is eligible");
 ok&=Check(!Eligible(false,true,true,true,true,true,false,100)&&!Eligible(true,false,true,true,true,true,false,100),"Sam and civilians excluded by guard and hostility gates");
 ok&=Check(!Eligible(true,true,false,true,true,true,false,100),"other levels excluded");
 ok&=Check(!Eligible(true,true,true,false,true,true,false,100),"unarmed NPC excluded");
 ok&=Check(!Eligible(true,true,true,true,false,true,false,100),"unconscious and interaction states excluded");
 ok&=Check(!Eligible(true,true,true,true,true,false,false,100),"invalid controller excluded");
 ok&=Check(!Eligible(true,true,true,true,true,true,true,100)&&!Eligible(true,true,true,true,true,true,false,0),"deleted and dead NPCs excluded");
 Candidate a[]={{1,0,0,0},{2,100,0,0},{3,20,0,0},{4,3000,0,0}};
 ok&=Check(Nearest(a,4,0)==2,"nearest eligible target selected without targeting self");
 ok&=Check(Nearest(a,4,3)==-1,"distant isolated guards get no attack order");
 ok&=Check(Nearest(a,1,0)==-1,"single guard cannot attack itself");
 a[2].id=1;ok&=Check(Nearest(a,3,0)==1,"duplicate actor identity cannot be targeted");
 a[1].x=NAN;ok&=Check(Nearest(a,2,0)==-1,"nonfinite distances rejected");
 ok&=Check(MaintainSession(1000,31000,true,true),"maintenance continues throughout active combat window");
 ok&=Check(!MaintainSession(31000,31000,true,true)&&!MaintainSession(32000,31000,true,true),"deadline stops maintenance exactly at thirty seconds");
 ok&=Check(!MaintainSession(1000,31000,false,true),"changed player or viewport ends maintenance");
 ok&=Check(!MaintainSession(1000,31000,true,false),"travel or level identity change ends maintenance");
 Candidate survivors[]={{1,0,0,0},{3,200,0,0}};
 ok&=Check(Nearest(survivors,2,0)==1,"remaining guard replaces a removed opponent");
 return ok?0:1;}
