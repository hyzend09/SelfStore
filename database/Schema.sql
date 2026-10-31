-- Run in the application database. Also embedded and applied by the app.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
-- Serialize schema initialization by simultaneous clients.
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock @Resource = N'SelfStorage.Schema',
    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
IF @lockResult < 0 THROW 50001, 'Cannot acquire schema initialization lock.', 1;

IF OBJECT_ID(N'dbo.StoreState', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoreState (
        Id int NOT NULL CONSTRAINT PK_StoreState PRIMARY KEY CONSTRAINT CK_StoreState_Id CHECK (Id = 1),
        SchemaVersion int NOT NULL,
        Revision bigint NOT NULL
    );
    INSERT dbo.StoreState (Id, SchemaVersion, Revision) VALUES (1, 1, 0);
END;

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    CREATE TABLE dbo.Users (
        Id int NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        FullName nvarchar(200) NOT NULL,
        Email nvarchar(320) COLLATE Latin1_General_100_CI_AS NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
        PasswordHash varchar(64) NOT NULL,
        Role varchar(20) NOT NULL CONSTRAINT CK_Users_Role CHECK (Role IN ('Customer', 'Admin'))
    );

IF OBJECT_ID(N'dbo.Facilities', N'U') IS NULL
    CREATE TABLE dbo.Facilities (
        Id int NOT NULL CONSTRAINT PK_Facilities PRIMARY KEY,
        Name nvarchar(200) COLLATE Latin1_General_100_CI_AS NOT NULL CONSTRAINT UQ_Facilities_Name UNIQUE,
        Address nvarchar(500) NOT NULL
    );

IF OBJECT_ID(N'dbo.StorageUnits', N'U') IS NULL
    CREATE TABLE dbo.StorageUnits (
        Id int NOT NULL CONSTRAINT PK_StorageUnits PRIMARY KEY,
        FacilityId int NOT NULL CONSTRAINT FK_StorageUnits_Facilities REFERENCES dbo.Facilities(Id),
        Code nvarchar(100) COLLATE Latin1_General_100_CI_AS NOT NULL,
        Type nvarchar(100) NOT NULL,
        SizeM2 float NOT NULL CONSTRAINT CK_StorageUnits_Size CHECK (SizeM2 > 0),
        PricePerMonth decimal(18,2) NOT NULL CONSTRAINT CK_StorageUnits_Price CHECK (PricePerMonth > 0),
        IsAvailable bit NOT NULL,
        CONSTRAINT UQ_StorageUnits_Facility_Code UNIQUE (FacilityId, Code)
    );

IF OBJECT_ID(N'dbo.Rentals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Rentals (
        Id int NOT NULL CONSTRAINT PK_Rentals PRIMARY KEY,
        UserId int NOT NULL CONSTRAINT FK_Rentals_Users REFERENCES dbo.Users(Id),
        UnitId int NOT NULL CONSTRAINT FK_Rentals_StorageUnits REFERENCES dbo.StorageUnits(Id),
        StartDate datetime2 NOT NULL,
        Months int NOT NULL CONSTRAINT CK_Rentals_Months CHECK (Months > 0 AND Months <= 1200),
        Deposit decimal(18,2) NOT NULL CONSTRAINT CK_Rentals_Deposit CHECK (Deposit >= 0),
        RentFee decimal(18,2) NOT NULL CONSTRAINT CK_Rentals_RentFee CHECK (RentFee >= 0),
        PaymentMethod nvarchar(100) NOT NULL,
        PaidAt datetime2 NOT NULL,
        QrContent nvarchar(1000) NOT NULL,
        IsReturned bit NOT NULL,
        ReturnedAt datetime2 NULL,
        CONSTRAINT CK_Rentals_Return CHECK (
            (IsReturned = 0 AND ReturnedAt IS NULL) OR (IsReturned = 1 AND ReturnedAt IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_Rentals_ActiveUnit ON dbo.Rentals(UnitId) WHERE IsReturned = 0;
    CREATE INDEX IX_Rentals_UserId ON dbo.Rentals(UserId);
END;
COMMIT;
