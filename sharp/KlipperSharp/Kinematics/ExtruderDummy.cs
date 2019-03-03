using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp.Kinematics
{
	// Dummy extruder class used when a printer has no extruder at all
	public class ExtruderDummy : ExtruderBase
	{
		public override double set_active(double print_time, bool is_active)
		{
			return 0.0;
		}

		public override void motor_off(double move_time)
		{
		}

		public override void check_move(Move move)
		{
			throw EndstopException.EndstopMoveError(move.end_pos, "Extrude when no extruder present");
		}

		public override double calc_junction(Move prev_move, Move move)
		{
			return move.max_cruise_v2;
		}

		public override int lookahead(List<Move> moves, int flush_count, bool lazy)
		{
			return flush_count;
		}

		public override void move(double print_time, Move move)
		{
			throw new NotImplementedException();
		}
	}

}
