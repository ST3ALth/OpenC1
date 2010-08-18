﻿using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using NFSEngine;
using PlatformEngine;
using Microsoft.Xna.Framework.Graphics;

namespace Carmageddon
{
    enum CpuDriverState
    {
        Racing,
        Attacking,
        ReturningToTrack
    }

    class CpuDriver : IDriver
    {
        public Vehicle Vehicle { get; set; }
        public bool InPlayersView;
        public float DistanceFromPlayer;
        public bool IsDead;
        public float LastPlayerTouchTime;

        CpuDriverState _state;
        OpponentPathNode _targetNode;
        OpponentPath _currentPath, _nextPath;
        Vector3 _lastPosition;
        float _lastPositionTime, _lastStateChangeTime;
        float _nextDirectionChangeTime;
        float _lastTargetChangeTime;
        float _reverseTurning;
        int _nbrFails = -1;
        float _lastDistance;
        float _maxSpeedAtEndOfPath = 0;
        bool _isReversing;
        Vector3 _closestPointOnPath;
        
        bool _raceStarted = false;

        public CpuDriver()
        {

        }

        public bool ModerateSteeringAtSpeed { get { return false; } }

        public void OnRaceStart()
        {
            _lastStateChangeTime = Engine.TotalSeconds;
            Vehicle.Chassis.Motor.Gearbox.CurrentGear = 1;
            LogPosition(Vehicle.Position);
            //SetTarget(OpponentController.GetClosestRaceNode(_lastPosition));
            _raceStarted = true;
        }

        public void Update()
        {
            if (IsDead)
            {
                Vehicle.Chassis.Brake(0);
                Vehicle.Chassis.Steer(0);
                return;
            }

            if (!_raceStarted) return;

            Vector3 pos = Vehicle.Position;
            bool isBraking = false;

            // check position
            if (_lastPositionTime + 1.5f < Engine.TotalSeconds)
            {
                float distFromLastPosition = Vector3.Distance(_lastPosition, pos);
                if (distFromLastPosition < 2 && Vehicle.Chassis.Speed < 4)
                {
                    Escape(); //were stuck, try and escape
                }
                LogPosition(pos);
            }
            if (Vehicle.Chassis.Actor.GlobalPose.Up.Y < 0.002f && Vehicle.Chassis.Speed < 5)
            {
                Vehicle.Chassis.Reset();
                return;
            }

            // check for state change
            if (_nextDirectionChangeTime < Engine.TotalSeconds)
            {
                if (_isReversing)
                {
                    _isReversing = false;
                    LogPosition(pos);
                }
            }

            if (_lastStateChangeTime + 30 < Engine.TotalSeconds)
            {
                //SetState(Engine.Random.Next() % 3 == 0 ? CpuDriverState.Attacking : CpuDriverState.Racing);
            }

            float distanceFromNode=0;
            if (_state == CpuDriverState.Racing) distanceFromNode = Vector3.Distance(pos, _targetNode.Position);
            else if (_state == CpuDriverState.ReturningToTrack) distanceFromNode = Vector3.Distance(pos, _closestPointOnPath);

            // if we've been trying to get to the same target for 20 seconds, get a new one
            if (_lastTargetChangeTime + 20 < Engine.TotalSeconds)
            {
                if (_lastDistance < distanceFromNode) //only get another node if were not getting closer
                {
                    GotoClosestNode(pos);
                    return;
                }
            }

            if (_state == CpuDriverState.Racing)
            {
                if (_currentPath != null)
                {
                    GameConsole.WriteLine("Limits " + _currentPath.Number + ", " + _currentPath.MinSpeedAtEnd + ", " + _maxSpeedAtEndOfPath);
                }

                if (_currentPath != null && Vehicle.Chassis.Speed > _maxSpeedAtEndOfPath)
                {
                    float distToBrake = Vehicle.Chassis.Speed * 0.45f + ((Vehicle.Chassis.Speed - _maxSpeedAtEndOfPath) * 1.4f);
                    //GameConsole.WriteLine("brake: " + (int)distToBrake + ", " + (int)distanceFromNode);
                    //Matrix mat = Matrix.CreateTranslation(0, 0, distToBrake) * Vehicle.Chassis.Actor.GlobalPose;

                    if (distToBrake >= distanceFromNode)
                    {
                        Vehicle.Chassis.Brake(1);
                        isBraking = true;
                    }
                }
                if (_currentPath != null)
                {
                    _closestPointOnPath = Helpers.GetClosestPointOnLine(_currentPath.Start.Position, _currentPath.End.Position, pos);
                    _closestPointOnPath.Y = pos.Y; //ignore Y
                    if (Vector3.Distance(_closestPointOnPath, pos) > _currentPath.Width)
                    {
                        _state = CpuDriverState.ReturningToTrack;
                    }
                }

                // now see if we're at the target ignoring height (if we jump over it for example)
                distanceFromNode = Vector2.Distance(new Vector2(pos.X, pos.Z), new Vector2(_targetNode.Position.X, _targetNode.Position.Z));
                if (distanceFromNode < 10 && pos.Y >= _targetNode.Position.Y)
                {
                    _nbrFails = 0; //reset fail counter

                    if (_currentPath != null)
                    {
                        if (Vehicle.Chassis.Speed < _currentPath.MinSpeedAtEnd)
                        {
                            Vehicle.Chassis.Boost();
                        }
                    }
                    _currentPath = _nextPath;

                    if (_currentPath != null && _currentPath.Type == PathType.Cheat)
                    {
                        Vehicle.Chassis.Actor.GlobalPosition = _currentPath.End.Position;
                        Vehicle.Chassis.Reset();
                        _state = CpuDriverState.Racing;
                        return;
                    }

                    if (_nextPath == null)  // the node didnt have any start paths
                    {
                        GotoClosestNode(pos);
                    }
                    else
                    {
                        SetTarget(_currentPath.End);
                    }

                    if (_currentPath == null && _nextPath == null)
                    {
                        Teleport(); //if the node we've just got to doesnt have any outgoing paths, teleport randomly
                    }
                }
            }
            else if (_state == CpuDriverState.ReturningToTrack)
            {
                _closestPointOnPath = Helpers.GetClosestPointOnLine(_currentPath.Start.Position, _currentPath.End.Position, pos);
                _closestPointOnPath.Y = pos.Y; //ignore Y
                Engine.DebugRenderer.AddCube(Matrix.CreateTranslation(_closestPointOnPath), Color.Blue);
                if (Vector3.Distance(_closestPointOnPath, pos) < _currentPath.Width)
                {
                    _state = CpuDriverState.Racing;
                }
            }

            GameConsole.WriteLine("Dist", Vector3.Distance(_closestPointOnPath, pos));

            Vector3 towardsNode = Vector3.Zero;
            if (_state == CpuDriverState.Racing)
            {
                towardsNode = _targetNode.Position - pos;
            }
            else if (_state == CpuDriverState.Attacking)
            {
                towardsNode = Race.Current.PlayerVehicle.Position - pos;
            }
            else if (_state == CpuDriverState.ReturningToTrack)
            {
                towardsNode = _closestPointOnPath - pos;
            }

            GameConsole.WriteLine("state: " + _state);

            float angle = GetSignedAngleBetweenVectors(Vehicle.Chassis.Actor.GlobalOrientation.Forward, towardsNode);
            angle *= 1.5f;
            if (angle > 1) angle = 1;
            else if (angle < -1) angle = -1;

            if (Math.Abs(angle) > 0.003f) Vehicle.Chassis.Steer(angle);

            if (!isBraking)
            {
                if (_state == CpuDriverState.ReturningToTrack)
                {
                    if (Vehicle.Chassis.Speed < 20)
                        Vehicle.Chassis.Accelerate(0.2f);
                    else
                        if (Math.Abs(angle) > 0.5f) Vehicle.Chassis.Brake(1);
                }
                else
                {
                    if (Math.Abs(angle) > 0.7f)
                        Vehicle.Chassis.Accelerate(0.5f); //if were turning hard, go easy on the gas pedal
                    else
                        Vehicle.Chassis.Accelerate(1.0f);
                }
            }

            _lastDistance = distanceFromNode;

            if (_isReversing)
            {
                Vehicle.Chassis.Brake(0.5f);
                Vehicle.Chassis.Steer(_reverseTurning);
            }            

            Engine.DebugRenderer.AddWireframeCube(Matrix.CreateScale(2) * Matrix.CreateTranslation(_targetNode.Position), Color.Green);
        }

        public void SetState(CpuDriverState state)
        {
            _state = state;
            GameConsole.WriteEvent(state.ToString());
            _lastStateChangeTime = Engine.TotalSeconds;
        }

        private void LogPosition(Vector3 pos)
        {
            _lastPosition = pos;
            _lastPositionTime = Engine.TotalSeconds;
        }

        private void GotoClosestNode(Vector3 pos)
        {
            OpponentPathNode curNode = _targetNode;
            SetTarget(OpponentController.GetClosestNode(pos));
            _currentPath = null;

            if (curNode == _targetNode) //if the closest node is the one we've failed to get to
            {
                // if we've failed to get to the target twice we're really stuck, teleport straight to node :)
                Teleport(_targetNode);
                return;
            }
            GameConsole.WriteEvent("ClosestNode");
            _nbrFails++;
            //_currentPath = _nextPath = null;
        }

        public void SetTarget(OpponentPathNode node)
        {
            _targetNode = node;
            _state = CpuDriverState.Racing;
            _lastTargetChangeTime = Engine.TotalSeconds;
            GetNextPath();
        }

        private void Escape()
        {
            _isReversing = !_isReversing;
            _nextDirectionChangeTime = Engine.TotalSeconds + Engine.Random.Next(1.5f, 3f);
            _reverseTurning = Engine.Random.Next(-1f, 0f);
        }

        private void Teleport()
        {
            Teleport(OpponentController.GetRandomNode());
        }
        private void Teleport(OpponentPathNode node)
        {
            SetTarget(node);
            Vehicle.Chassis.Actor.GlobalPosition = _targetNode.Position;
            Vehicle.Chassis.Reset();
        }

        private void GetNextPath()
        {
            // for the next node, look at direction we will be turning and decide if we will need to slow down before we get there
            _nextPath = OpponentController.GetNextPath(_targetNode);
            

            if (_nextPath != null && _currentPath != null)
            {
                float nextPathAngle = MathHelper.ToDegrees(GetUnsignedAngleBetweenVectors(_currentPath.End.Position - _currentPath.Start.Position, _nextPath.End.Position - _nextPath.Start.Position));

                if (nextPathAngle > 5)
                {
                    float newspeed = (180 - nextPathAngle) * 0.55f;
                    _maxSpeedAtEndOfPath = newspeed;
                }
                else
                {
                    _maxSpeedAtEndOfPath = 255;
                }
            }
        }


        public static float GetSignedAngleBetweenVectors(Vector3 from, Vector3 to)
        {

            from.Y = to.Y = 0;
            from.Normalize();
            to.Normalize();
            Vector3 toRight = Vector3.Cross(to, Vector3.Up);
            toRight.Normalize();

            float forwardDot = Vector3.Dot(from, to);
            float rightDot = Vector3.Dot(from, toRight);

            // Keep dot in range to prevent rounding errors
            forwardDot = MathHelper.Clamp(forwardDot, -1.0f, 1.0f);

            double angleBetween = Math.Acos(forwardDot);

            if (rightDot < 0.0f)
                angleBetween *= -1.0f;

            return (float)angleBetween;
        }

        public float GetUnsignedAngleBetweenVectors(Vector3 from, Vector3 to)
        {
            //from.Y = to.Y = 0;
            from.Normalize();
            to.Normalize();

            Vector2 a = new Vector2(from.X, from.Z);
            a.Normalize();
            Vector2 b = new Vector2(to.X, to.Z);
            b.Normalize();
            //return (float)Math.Acos(Vector2.Dot(a, b));
            return (float)Math.Acos(Vector3.Dot(from, to));
        }
    }
}
