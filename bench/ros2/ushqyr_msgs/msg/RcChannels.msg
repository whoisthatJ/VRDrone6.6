# What a transmitter sends: 16 channels in microseconds, 1000..2000.
#   0 roll      1500 centre, 2000 = right
#   1 pitch     1500 centre, 2000 = forward (nose down)
#   2 throttle  1000 = zero, 2000 = full
#   3 yaw       1500 centre, 2000 = right (clockwise seen from above)
#   4 AUX1 arm  above 1500 = armed (arms only when it goes high while throttle is at or below 1050)
#   5 AUX2 mode below 1300 Angle, 1300..1700 Horizon, above 1700 Acro
#   6..15 spare
# A value of 0 means "not given": the channel is centred (throttle and AUX low).
std_msgs/Header header
uint16[16] channels
