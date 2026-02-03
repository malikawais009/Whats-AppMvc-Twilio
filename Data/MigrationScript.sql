-- WhatsAppMvcComplete Database Schema
-- SQL Server Database Migration Script
-- Run this script in SQL Server Management Studio or via sqlcmd

USE WhatsAppTwilioDb;
GO

-- Drop existing tables in correct order (by foreign key dependencies)
IF OBJECT_ID('MessageLogs', 'U') IS NOT NULL DROP TABLE MessageLogs;
IF OBJECT_ID('TemplateRequests', 'U') IS NOT NULL DROP TABLE TemplateRequests;
IF OBJECT_ID('Messages', 'U') IS NOT NULL DROP TABLE Messages;
IF OBJECT_ID('Templates', 'U') IS NOT NULL DROP TABLE Templates;
IF OBJECT_ID('Conversations', 'U') IS NOT NULL DROP TABLE Conversations;
IF OBJECT_ID('Users', 'U') IS NOT NULL DROP TABLE Users;
GO

-- Users Table (no dependencies)
CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Phone NVARCHAR(20) NOT NULL,
    WhatsAppNumber NVARCHAR(20) NULL,
    Email NVARCHAR(100) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
GO

-- Templates Table (no dependencies)
CREATE TABLE Templates (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Channel INT NOT NULL DEFAULT 1,
    TemplateText NVARCHAR(MAX) NOT NULL,
    Status INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(100) NULL,
    RejectionReason NVARCHAR(500) NULL
);
GO

-- Conversations Table (no dependencies)
CREATE TABLE Conversations (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PhoneNumber NVARCHAR(50) NOT NULL,
    LastMessageAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
GO

-- Messages Table (depends on Users, Templates, Conversations)
CREATE TABLE Messages (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    UserId INT NULL,
    ConversationId INT NULL,
    Channel INT NOT NULL,
    MessageText NVARCHAR(MAX) NOT NULL,
    Status INT NOT NULL DEFAULT 0,
    TwilioMessageId NVARCHAR(100) NULL,
    ScheduledAt DATETIME2 NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    RetryCount INT NOT NULL DEFAULT 0,
    TemplateId INT NULL,
    
    CONSTRAINT FK_Messages_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL,
    CONSTRAINT FK_Messages_Conversations FOREIGN KEY (ConversationId) REFERENCES Conversations(Id) ON DELETE SET NULL,
    CONSTRAINT FK_Messages_Templates FOREIGN KEY (TemplateId) REFERENCES Templates(Id) ON DELETE SET NULL
);
GO

-- TemplateRequests Table (depends on Templates)
CREATE TABLE TemplateRequests (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TemplateId INT NOT NULL,
    RequestedBy NVARCHAR(100) NOT NULL,
    RequestedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ApprovedBy NVARCHAR(100) NULL,
    ApprovedAt DATETIME2 NULL,
    Status INT NOT NULL DEFAULT 0,
    Comments NVARCHAR(500) NULL,
    
    CONSTRAINT FK_TemplateRequests_Templates FOREIGN KEY (TemplateId) REFERENCES Templates(Id) ON DELETE CASCADE
);
GO

-- MessageLogs Table (depends on Messages)
CREATE TABLE MessageLogs (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    MessageId INT NOT NULL,
    EventType INT NOT NULL,
    EventTimestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    WebhookPayload NVARCHAR(MAX) NULL,
    ErrorMessage NVARCHAR(500) NULL,
    
    CONSTRAINT FK_MessageLogs_Messages FOREIGN KEY (MessageId) REFERENCES Messages(Id) ON DELETE CASCADE
);
GO

-- Create indexes for better performance
CREATE INDEX IX_Messages_UserId ON Messages(UserId);
CREATE INDEX IX_Messages_ConversationId ON Messages(ConversationId);
CREATE INDEX IX_Messages_Status ON Messages(Status);
CREATE INDEX IX_Messages_ScheduledAt ON Messages(ScheduledAt);
CREATE INDEX IX_Messages_TwilioMessageId ON Messages(TwilioMessageId);
CREATE INDEX IX_MessageLogs_MessageId ON MessageLogs(MessageId);
CREATE INDEX IX_MessageLogs_EventTimestamp ON MessageLogs(EventTimestamp);
CREATE INDEX IX_Templates_Status ON Templates(Status);
CREATE INDEX IX_Conversations_PhoneNumber ON Conversations(PhoneNumber);
CREATE INDEX IX_Users_Phone ON Users(Phone);
CREATE INDEX IX_Users_WhatsAppNumber ON Users(WhatsAppNumber);
GO

-- ============================================
-- SEED DATA
-- ============================================

-- Insert sample users
INSERT INTO Users (Name, Phone, WhatsAppNumber, Email, CreatedAt)
VALUES 
('John Doe', '+1234567890', '+1234567890', 'john.doe@example.com', GETUTCDATE()),
('Jane Smith', '+0987654321', '+0987654321', 'jane.smith@example.com', GETUTCDATE()),
('Bob Wilson', '+1122334455', '+1122334455', 'bob.wilson@example.com', GETUTCDATE()),
('Alice Brown', '+5544332211', '+5544332211', 'alice.brown@example.com', GETUTCDATE()),
('Charlie Davis', '+6677889900', '+6677889900', 'charlie.davis@example.com', GETUTCDATE());
GO

-- Insert sample templates
INSERT INTO Templates (Name, Channel, TemplateText, Status, CreatedBy, CreatedAt)
VALUES 
('welcome_message', 1, 'Hello {{name}}! Welcome to our service. Your account has been created successfully.', 1, 'System', GETUTCDATE()),
('order_confirmation', 1, 'Hi {{name}}, your order #{{order_id}} has been confirmed! We will notify you when it ships.', 1, 'System', GETUTCDATE()),
('shipping_notification', 1, 'Great news {{name}}! Your order #{{order_id}} has been shipped and is on its way!', 1, 'System', GETUTCDATE()),
('password_reset', 1, '{{name}}, your password reset code is: {{code}}. This code expires in 24 hours.', 0, 'System', GETUTCDATE()),
('promotional_offer', 1, 'Hello {{name}}! Special offer just for you: {{discount}}% off on your next purchase!', 0, 'Marketing', GETUTCDATE()),
('appointment_reminder', 1, 'Hi {{name}}, this is a reminder for your appointment on {{date}} at {{time}}. Reply CONFIRM to confirm.', 1, 'System', GETUTCDATE());
GO

-- Insert sample conversations
INSERT INTO Conversations (PhoneNumber, LastMessageAt)
VALUES 
('whatsapp:+1234567890', DATEADD(HOUR, -2, GETUTCDATE())),
('whatsapp:+0987654321', DATEADD(HOUR, -5, GETUTCDATE())),
('whatsapp:+1122334455', DATEADD(DAY, -1, GETUTCDATE()));
GO

-- Insert sample messages
INSERT INTO Messages (UserId, ConversationId, Channel, MessageText, Status, TwilioMessageId, CreatedAt, RetryCount)
VALUES 
(1, 1, 1, 'Hello! How can I help you today?', 2, 'SM1234567890abcdef', GETUTCDATE(), 0),
(1, 1, 1, 'Thank you for your quick response!', 2, 'SM0987654321fedcba', GETUTCDATE(), 0),
(2, 2, 1, 'I have a question about my order.', 4, NULL, GETUTCDATE(), 0),
(3, NULL, 0, 'Your OTP is 123456', 1, 'SM111111111111111', GETUTCDATE(), 0),
(NULL, 3, 1, 'I want to learn more about your services.', 4, NULL, DATEADD(DAY, -1, GETUTCDATE()), 0);
GO

-- Insert sample message logs
INSERT INTO MessageLogs (MessageId, EventType, EventTimestamp, WebhookPayload)
VALUES 
(1, 4, GETUTCDATE(), '{"MessageSid": "SM1234567890abcdef", "Status": "sent"}'),
(1, 0, DATEADD(MINUTE, 1, GETUTCDATE()), '{"MessageSid": "SM1234567890abcdef", "Status": "delivered"}'),
(2, 4, GETUTCDATE(), '{"MessageSid": "SM0987654321fedcba", "Status": "sent"}'),
(3, 3, GETUTCDATE(), '{"From": "whatsapp:+0987654321", "Body": "I have a question about my order.", "MessageSid": "SM222222222222222"}'),
(4, 4, GETUTCDATE(), '{"MessageSid": "SM111111111111111", "Status": "sent"}'),
(5, 3, DATEADD(DAY, -1, GETUTCDATE()), '{"From": "whatsapp:+1122334455", "Body": "I want to learn more about your services.", "MessageSid": "SM333333333333333"}');
GO

-- Insert sample template requests
INSERT INTO TemplateRequests (TemplateId, RequestedBy, RequestedAt, Status)
VALUES 
(4, 'System', GETUTCDATE(), 1),
(5, 'Marketing', GETUTCDATE(), 0),
(6, 'System', GETUTCDATE(), 1);
GO

-- Print summary
PRINT '========================================';
PRINT 'Database setup completed successfully!';
PRINT '========================================';
PRINT '';

DECLARE @UserCount INT = (SELECT COUNT(*) FROM Users);
DECLARE @TemplateCount INT = (SELECT COUNT(*) FROM Templates);
DECLARE @MessageCount INT = (SELECT COUNT(*) FROM Messages);
DECLARE @ConversationCount INT = (SELECT COUNT(*) FROM Conversations);

PRINT 'Users: ' + CAST(@UserCount AS VARCHAR(10));
PRINT 'Templates: ' + CAST(@TemplateCount AS VARCHAR(10));
PRINT 'Messages: ' + CAST(@MessageCount AS VARCHAR(10));
PRINT 'Conversations: ' + CAST(@ConversationCount AS VARCHAR(10));
GO
