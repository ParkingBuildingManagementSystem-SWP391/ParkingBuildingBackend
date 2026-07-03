IF OBJECT_ID(N'[dbo].[MembershipSlots]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MembershipSlots]
    (
        [MembershipSlotId] INT IDENTITY(1,1) NOT NULL,
        [MembershipCardId] INT NOT NULL,
        [SlotId] INT NOT NULL,
        CONSTRAINT [PK_MembershipSlots] PRIMARY KEY CLUSTERED ([MembershipSlotId] ASC),
        CONSTRAINT [FK_MembershipSlots_MembershipCards]
            FOREIGN KEY ([MembershipCardId]) REFERENCES [dbo].[MembershipCards] ([MembershipCardId]),
        CONSTRAINT [FK_MembershipSlots_ParkingSlots]
            FOREIGN KEY ([SlotId]) REFERENCES [dbo].[ParkingSlots] ([SlotId]),
        CONSTRAINT [UQ_MembershipSlots_Card_Slot] UNIQUE ([MembershipCardId], [SlotId])
    );
END;
GO

IF COL_LENGTH(N'[dbo].[MembershipCards]', N'SlotId') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[MembershipSlots]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[MembershipSlots] ([MembershipCardId], [SlotId])
    SELECT [MembershipCardId], [SlotId]
    FROM [dbo].[MembershipCards] AS mc
    WHERE [SlotId] IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM [dbo].[MembershipSlots] AS ms
          WHERE ms.[MembershipCardId] = mc.[MembershipCardId]
            AND ms.[SlotId] = mc.[SlotId]
      );
END;
GO
