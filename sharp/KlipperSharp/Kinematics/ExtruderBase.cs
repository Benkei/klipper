using System;
using System.Collections.Generic;
using System.Linq;

namespace KlipperSharp.Kinematics
{
	public abstract class ExtruderBase
	{
		public abstract double set_active(double print_time, bool is_active);

		public abstract void motor_off(double move_time);

		public abstract void check_move(Move move);

		public abstract double calc_junction(Move prev_move, Move move);

		public abstract int lookahead(List<Move> moves, int flush_count, bool lazy);

		public abstract void move(double print_time, Move move);
	}
}
