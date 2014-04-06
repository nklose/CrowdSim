using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Simulation
{
	public class Tile : IPathNode<Tile>
	{
		public const float TileSize = 0.75f;		// Should change to match the width of our model

		private const float FlatTolerance = 0.05f;	// I have no idea what our tolerance for y-variation in terrain should be.
		private const float Clearance = 3.0f;		// The minimum clearance over the ground for a tile to be considered IsWalkable

		public Tile (Vector3 corner, int x, int z, ref Mesh mesh)
		{
			X = x;
			Z = z;

			Position = new Vector3(corner.x + TileSize / 2, corner.y, corner.z + TileSize / 2);

			IsWalkable = TestWalkability(ref mesh);
			claims = new List<TileClaim>();
		}

		// Basic Tile properties
		public bool IsWalkable { get; set; }
		public bool IsEntrance { get; set; }
		public bool IsExit { get; set; }

		#region IPathNode members
		public bool Invalid { get { return !IsWalkable; } }
		public List<Tile> Connections { get; set; }

		public float GetDistanceHeuristic(Tile other)
		{
			// Finds the number of orthogonal tile movements that would
			// be required to pass between the two tiles without taking
			// obstacles into account.
			return Math.Abs(other.X - X) + Math.Abs(other.Z - Z);
		}

		public float GetDistance(Tile other, int framenum, float speed)
		{
			float dist = Vector3.Distance(Position, other.Position);
			int framesOfMovement = (int)(dist / speed);

			return framesOfMovement + other.FindDelay(framenum + framesOfMovement/2, framesOfMovement);
		}

		public List<IPathAction<Tile>> GetPathTo(Tile other, float speed, ref int time)
		{
			List<IPathAction<Tile>> list = new List<IPathAction<Tile>>();

			float dist = Vector3.Distance(Position, other.Position);
			int framesOfMovement = (int)(dist / speed);
			int delay = other.FindDelay(time + framesOfMovement / 2, framesOfMovement);

			if (delay > 0)
			{
				list.Add(new PathfindingDelay() { Origin = this, Delay = delay });
			}

			list.Add(new PathfindingMovement() { Origin = this, Destination = other });

			time += framesOfMovement + delay;
			return list;
		}
		#endregion

		public void Update()
		{
			claims.ForEach(c => { --c.StartTime; --c.EndTime; });
			claims.RemoveAll(c => c.EndTime <= 0);
		}

		// The center of the tile
		public Vector3 Position { get; private set; }

		// The tile's position in the grid
		public int X { get; private set; }
		public int Z { get; private set; }	// We'll continue using Z instead of Y to remain consistent.

		// Agents can "claim" the tile a certain number of updates in advance.
		// We keep track of when each one will be in the tile so that others can
		// move through it up until that point.
		private List<TileClaim> claims;
		public void AddClaim(int start, int duration)
		{
			claims.Add(new TileClaim(){ StartTime = start, EndTime = start+duration });
			claims.OrderBy(c => c.StartTime);
		}

		public bool CurrentlyClaimed()
		{
			var first = claims.FirstOrDefault();

			// If the start time of the first element is greater than 0 then that
			// claim hasn't started yet. Since we constantly sort the claims, the
			// first one is guaranteed to have the lowest start time.
			return !(first == null || first.StartTime > 0);
		}

		public bool MultipleClaims()
		{
			return (claims.Count(c => c.StartTime <= 0) > 1);
		}

		// The action to take when an agent reaches it's goal.
		// TODO: Add more action types to make the simulation more realistic. This 
		// may have to wait until after the project is over.
		public Action<Agent> GetTileAction()
		{
			if (IsExit)
			{
				return new Action<Agent>(a => a.Unspawn());
			}

			return null;
		}

		public bool TestWalkability(ref Mesh mesh)
		{
			// Throws a sphere at the terrain to detect obstacles. This should also detect any static objects 
			// that get added to the scene, allowing users to spruce up the environment. This is a simple test,
			// but will fail for anything without a collider, and won't detect the lack of a floor.
			// I'm commenting this out for now for speed reasons.
			//if (Physics.SphereCastAll(Position - new Vector3(0, TileSize, 0), TileSize / 2, Vector3.up).Any())
			//{
			//	return false;
			//}

			// Determine whether tile is in mesh. We may be dealing with concave meshes, which don't support mesh
			// colliders properly.
			Vector3 validBoxSize = new Vector3(TileSize / 2, FlatTolerance, TileSize / 2);

			//string size = String.Format("{0} :: {1}", Position, validBoxSize);
			//Debug.LogWarning(size);

			// The position and dimensions of the box that represents the space that an obstacle has to occupy in order to be considered a problem for pathing
			Vector3 invalidBoxCenter = new Vector3(Position.x, Position.y + FlatTolerance + (Clearance - FlatTolerance) / 2, Position.z);
			Vector3 invalidBoxSize = new Vector3(TileSize / 2, (Clearance - FlatTolerance) / 2, TileSize / 2);

			// Whether this tile contains actual ground
			bool containedInMesh = false;

			int index = 0;
			while (index < mesh.triangles.Count())
			{
				Vector3[] vertices = { mesh.vertices[mesh.triangles[index]],
										 mesh.vertices[mesh.triangles[index+1]], 
										 mesh.vertices[mesh.triangles[index+2]] };

				/*if (MeshUtilities.TestTriangleBoxOverlap(invalidBoxCenter, invalidBoxSize, vertices))
				{
					Debug.Log("Bad touch");
					return false;
				}
				else*/ if (!containedInMesh && MeshUtilities.TestTriangleBoxOverlap(Position, validBoxSize, vertices))
				{
					//Debug.Log("Good touch");
					containedInMesh = true;
					return true;
				}

				index += 3;
			}

			return containedInMesh;
		}

		// Finds how long an agent will have to delay if it wants to enter this tile at a time starting at
		// time and entering the tile will take duration time.
		private int FindDelay(int time, int duration)
		{
			// The number of frames the agent will have to wait before it
			// can enter the tile.
			int delay = 0;

			foreach (TileClaim claim in claims)
			{
				// Check to see whether our journey will interfere with an existing claim
				if ((claim.StartTime <= time + delay && claim.EndTime >= time + delay)
				    || (claim.StartTime <= time + duration + delay && claim.EndTime >= time + duration + delay)
				    || (claim.StartTime >= time + delay && claim.EndTime <= time + duration + delay))
				{
					delay += claim.EndTime - time;
				}
			}

			return delay;
		}

		public void DebugDraw()
		{
			if (Simulation.Debug)
			{
				var upperLeft = new Vector3(Position.x - TileSize / 2, Position.y, Position.z + TileSize / 2);
				var upperRight = new Vector3(Position.x + TileSize / 2, Position.y, Position.z + TileSize / 2);
				var lowerLeft = new Vector3(Position.x - TileSize / 2, Position.y, Position.z - TileSize / 2);
				var lowerRight = new Vector3(Position.x + TileSize / 2, Position.y, Position.z - TileSize / 2);

				// These points are the midpoints between the center and the edges - if they
				// are on the edge lines it becomes too chaotic.
				var upperMid = new Vector3(Position.x, Position.y, Position.z + TileSize / 4);
				var lowerMid = new Vector3(Position.x, Position.y, Position.z - TileSize / 4);
				var leftMid = new Vector3(Position.x - TileSize / 4, Position.y, Position.z);
				var rightMid = new Vector3(Position.x + TileSize / 4, Position.y, Position.z);

				Color validColor = Color.blue;
				Color errorColor = Color.red;

				Debug.DrawLine(upperLeft, upperRight, validColor);
				Debug.DrawLine(upperRight, lowerRight, validColor);
				Debug.DrawLine(lowerRight, lowerLeft, validColor);
				Debug.DrawLine(lowerLeft, upperLeft, validColor);

				if (!IsWalkable)
				{
					Debug.DrawLine(upperLeft, lowerRight, errorColor);
					Debug.DrawLine(upperRight, lowerLeft, errorColor);
				}

				if (IsEntrance)
				{
					Debug.DrawLine(upperMid, lowerMid, validColor);
					Debug.DrawLine(leftMid, rightMid, validColor);
				}

				if (IsExit)
				{
					Debug.DrawLine(upperMid, rightMid, validColor);
					Debug.DrawLine(rightMid, lowerMid, validColor);
					Debug.DrawLine(lowerMid, leftMid, validColor);
					Debug.DrawLine(leftMid, upperMid, validColor);
				}

				if (MultipleClaims())
				{
					Debug.DrawLine(upperLeft, upperMid, errorColor);
					Debug.DrawLine(upperRight, upperMid, errorColor);
					Debug.DrawLine(lowerLeft, lowerMid, errorColor);
					Debug.DrawLine(lowerRight, lowerMid, errorColor);
					Debug.DrawLine(upperLeft, leftMid, errorColor);
					Debug.DrawLine(lowerLeft, leftMid, errorColor);
					Debug.DrawLine(upperRight, rightMid, errorColor);
					Debug.DrawLine(lowerRight, rightMid, errorColor);
				}
				else if (CurrentlyClaimed())
				{
					Debug.DrawLine(upperLeft, upperMid, validColor);
					Debug.DrawLine(upperRight, upperMid, validColor);
					Debug.DrawLine(lowerLeft, lowerMid, validColor);
					Debug.DrawLine(lowerRight, lowerMid, validColor);
					Debug.DrawLine(upperLeft, leftMid, validColor);
					Debug.DrawLine(lowerLeft, leftMid, validColor);
					Debug.DrawLine(upperRight, rightMid, validColor);
					Debug.DrawLine(lowerRight, rightMid, validColor);
				}
			}
		}
	}
}