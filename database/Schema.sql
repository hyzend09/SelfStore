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

IF EXISTS (SELECT 1 FROM dbo.StoreState WHERE SchemaVersion NOT IN (1, 2, 3, 4))
    THROW 50003, 'Unsupported SelfStorage schema version.', 1;

IF EXISTS (SELECT 1 FROM dbo.StoreState WHERE Id = 1 AND SchemaVersion = 1)
BEGIN
    ALTER TABLE dbo.Rentals ADD
        CustomerNameSnapshot nvarchar(200) NOT NULL CONSTRAINT DF_Rentals_CustomerSnapshot DEFAULT N'',
        UnitCodeSnapshot nvarchar(100) NOT NULL CONSTRAINT DF_Rentals_UnitSnapshot DEFAULT N'',
        FacilityNameSnapshot nvarchar(200) NOT NULL CONSTRAINT DF_Rentals_FacilitySnapshot DEFAULT N'',
        FacilityAddressSnapshot nvarchar(500) NOT NULL CONSTRAINT DF_Rentals_AddressSnapshot DEFAULT N'',
        ReceivedAt datetime2 NULL,
        ReceivedByUserId int NULL CONSTRAINT FK_Rentals_Receiver REFERENCES dbo.Users(Id),
        ReturnRequestedAt datetime2 NULL,
        InspectedAt datetime2 NULL,
        InspectedByUserId int NULL CONSTRAINT FK_Rentals_Inspector REFERENCES dbo.Users(Id),
        InspectionNotes nvarchar(1000) NOT NULL CONSTRAINT DF_Rentals_Notes DEFAULT N'',
        Deduction decimal(18,2) NOT NULL CONSTRAINT DF_Rentals_Deduction DEFAULT 0,
        RefundAmount decimal(18,2) NOT NULL CONSTRAINT DF_Rentals_Refund DEFAULT 0,
        RefundedAt datetime2 NULL,
        RefundedByUserId int NULL CONSTRAINT FK_Rentals_Refunder REFERENCES dbo.Users(Id),
        RefundReference nvarchar(200) NOT NULL CONSTRAINT DF_Rentals_Reference DEFAULT N'',
        IsLegacyRental bit NOT NULL CONSTRAINT DF_Rentals_Legacy DEFAULT 0;
    -- Dynamic SQL compiles after the new columns exist, including on a fresh database.
    EXEC(N'UPDATE r SET CustomerNameSnapshot = c.FullName, UnitCodeSnapshot = u.Code,
        FacilityNameSnapshot = f.Name, FacilityAddressSnapshot = f.Address, IsLegacyRental = 1
        FROM dbo.Rentals r JOIN dbo.Users c ON c.Id = r.UserId
        JOIN dbo.StorageUnits u ON u.Id = r.UnitId JOIN dbo.Facilities f ON f.Id = u.FacilityId;
        ALTER TABLE dbo.Rentals ADD CONSTRAINT CK_Rentals_Settlement CHECK (
            Deduction >= 0 AND Deduction <= Deposit AND RefundAmount >= 0 AND
            (InspectedAt IS NULL OR (ReturnRequestedAt IS NOT NULL AND RefundAmount = Deposit - Deduction AND LEN(LTRIM(RTRIM(InspectionNotes))) > 0)) AND
            (RefundedAt IS NULL OR (InspectedAt IS NOT NULL AND IsReturned = 1 AND LEN(LTRIM(RTRIM(RefundReference))) > 0)) AND
            (IsReturned = 0 OR IsLegacyRental = 1 OR RefundedAt IS NOT NULL));');
    UPDATE dbo.StoreState SET SchemaVersion = 2, Revision = CASE WHEN Revision = 0 THEN 0 ELSE Revision + 1 END WHERE Id = 1;
END;
IF EXISTS (SELECT 1 FROM dbo.StoreState WHERE Id = 1 AND SchemaVersion = 2)
BEGIN
    ALTER TABLE dbo.Users ADD IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1;
    ALTER TABLE dbo.Rentals ALTER COLUMN PaidAt datetime2 NULL;
    ALTER TABLE dbo.Rentals ADD
        HoldExpiresAt datetime2 NULL,
        HoldOutcome varchar(12) NULL,
        ExtraFee decimal(18,2) NOT NULL CONSTRAINT DF_Rentals_ExtraFee DEFAULT 0,
        RequiresMaintenance bit NOT NULL CONSTRAINT DF_Rentals_Maintenance DEFAULT 0;
    DROP INDEX UX_Rentals_ActiveUnit ON dbo.Rentals;
    EXEC(N'CREATE UNIQUE INDEX UX_Rentals_ActiveUnit ON dbo.Rentals(UnitId)
        WHERE IsReturned = 0 AND HoldOutcome IS NULL;
        ALTER TABLE dbo.Rentals ADD CONSTRAINT CK_Rentals_Hold CHECK (
            (PaidAt IS NOT NULL AND HoldOutcome IS NULL) OR
            (PaidAt IS NULL AND HoldExpiresAt IS NOT NULL AND QrContent = N'''' AND IsReturned = 0
                AND ReceivedAt IS NULL AND ReturnRequestedAt IS NULL AND (HoldOutcome IS NULL OR HoldOutcome IN (''Cancelled'', ''Expired''))));
        ALTER TABLE dbo.Rentals ADD CONSTRAINT CK_Rentals_ExtraFee CHECK (ExtraFee >= 0);');
    CREATE TABLE dbo.Payments (
        Id int NOT NULL CONSTRAINT PK_Payments PRIMARY KEY CONSTRAINT FK_Payments_Rentals REFERENCES dbo.Rentals(Id) ON DELETE CASCADE,
        Amount decimal(19,2) NOT NULL CONSTRAINT CK_Payments_Amount CHECK (Amount >= 0),
        TransactionCode varchar(80) COLLATE Latin1_General_100_CI_AS NOT NULL CONSTRAINT UQ_Payments_TransactionCode UNIQUE,
        Method nvarchar(100) NOT NULL,
        PaidAt datetime2 NOT NULL
    );
    UPDATE dbo.StoreState SET SchemaVersion = 3, Revision = CASE WHEN Revision = 0 THEN 0 ELSE Revision + 1 END WHERE Id = 1;
END;
IF EXISTS (SELECT 1 FROM dbo.StoreState WHERE Id = 1 AND SchemaVersion = 3)
BEGIN
    ALTER TABLE dbo.Users ALTER COLUMN PasswordHash varchar(160) NOT NULL;
    ALTER TABLE dbo.Rentals ADD PaidAtIsUtc bit NOT NULL CONSTRAINT DF_Rentals_PaidAtIsUtc DEFAULT 0;
    UPDATE dbo.StoreState SET SchemaVersion = 4, Revision = CASE WHEN Revision = 0 THEN 0 ELSE Revision + 1 END WHERE Id = 1;
END;
COMMIT;
