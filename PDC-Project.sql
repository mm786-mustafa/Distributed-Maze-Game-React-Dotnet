Create Database MazeGame;
use MazeGame;

-- Create DB
CREATE DATABASE IF NOT EXISTS MazeGame
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

-- Create app user
CREATE USER IF NOT EXISTS 'maze_user'@'localhost' IDENTIFIED BY 'StrongPass123';

-- Grant rights
GRANT ALL PRIVILEGES ON MazeGame.* TO 'maze_user'@'localhost';
FLUSH PRIVILEGES;

-- select * from moves;
-- select * from results;
