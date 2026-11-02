#pragma once
#include <cmath>
namespace MovePlacement {
struct Point {float x,y;};
inline bool Valid(int metres,int slot){return metres>=3&&metres<=6&&slot>=0&&slot<256;}
inline Point Offset(int metres,int slot,float yaw){
    int column=slot==0?0:((slot+1)/2)*(slot%2?1:-1);
    float forward=metres*100.0f,side=column*120.0f;
    return {std::cos(yaw)*forward-std::sin(yaw)*side,std::sin(yaw)*forward+std::cos(yaw)*side};
}
inline bool FarEnough(float x,float y,float yaw,int metres){return std::isfinite(x)&&std::isfinite(y)&&x*std::cos(yaw)+y*std::sin(yaw)>=metres*100.0f-0.5f;}
}
