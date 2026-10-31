-- Run against master using SSMS or sqlcmd. Safe to run again.
IF DB_ID(N'SelfStorage') IS NULL
    CREATE DATABASE [SelfStorage];
