CREATE DATABASE [MultiHostApplicationDB];
GO
USE [MultiHostApplicationDB]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ClientAgentCredentials](
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
	[ClientHostID] [bigint] NULL,
	[SecretHash] [binary](32) NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
	[ExpiresAt] [datetime] NULL,
	[RevokedAt] [datetime] NULL,
	[LastUsedAt] [datetime] NULL,
 CONSTRAINT [PK_ClientAgentCredentials] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ClientHostHeartbeatLogs](
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
	[ClientAgentID] [varchar](64) NOT NULL,
	[LoggedDateTime] [datetime] NOT NULL,
	[ResourceUsageMetrics] [nvarchar](max) NULL,
 CONSTRAINT [PK_ClientHostHeartbeatLogs] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ClientHosts](
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
	[ClientAgentID] [varchar](64) NOT NULL,
	[ClientAgentName] [nvarchar](300) NOT NULL,
	[ClientAgentOS] [varchar](50) NOT NULL,
	[ClientAgentVersion] [varchar](20) NOT NULL,
	[ClientRegistrationDateTime] [datetime] NOT NULL,
	[Status] [bit] NOT NULL,
	[IsDeleted] [bit] NOT NULL,
	[DeletedAt] [datetime] NULL,
 CONSTRAINT [PK_ClientHosts] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DeploymentInstructions](
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
	[PackageCode] [varchar](100) NOT NULL,
	[PackageName] [nvarchar](200) NOT NULL,
	[Version] [varchar](30) NOT NULL,
	[Platform] [varchar](30) NOT NULL,
	[InstallPlaybookYaml] [nvarchar](max) NOT NULL,
	[VerificationPlaybookYaml] [nvarchar](max) NOT NULL,
	[RollbackPlaybookYaml] [nvarchar](max) NULL,
	[DefaultConfiguration] [nvarchar](max) NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
 CONSTRAINT [PK_DeploymentInstructions] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_DeploymentInstructions_PackageCode_Version] UNIQUE NONCLUSTERED 
(
	[PackageCode] ASC,
	[Version] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DeploymentJobLogs](
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
	[DeploymentJobID] [bigint] NOT NULL,
	[LoggedAt] [datetime] NOT NULL,
	[EventName] [varchar](50) NOT NULL,
	[Severity] [varchar](20) NOT NULL,
	[Message] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_DeploymentJobLogs] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DeploymentJobs](
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
	[DeploymentInstructionID] [bigint] NOT NULL,
	[ClientHostID] [bigint] NOT NULL,
	[RequestedBy] [nvarchar](100) NULL,
	[Configuration] [nvarchar](max) NULL,
	[Status] [varchar](30) NOT NULL,
	[CurrentStep] [varchar](50) NULL,
	[RequestedAt] [datetime] NOT NULL,
	[StartedAt] [datetime] NULL,
	[CompletedAt] [datetime] NULL,
	[Success] [bit] NULL,
	[ErrorMessage] [nvarchar](max) NULL,
 CONSTRAINT [PK_DeploymentJobs] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
ALTER TABLE [dbo].[ClientAgentCredentials] ADD  CONSTRAINT [DF_ClientAgentCredentials_CreatedAt]  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ClientHostHeartbeatLogs] ADD  CONSTRAINT [DF_ClientHostHeartbeatLogs_LoggedDateTime]  DEFAULT (getdate()) FOR [LoggedDateTime]
GO
ALTER TABLE [dbo].[ClientHosts] ADD  CONSTRAINT [DF_ClientHosts_RegistrationDateTime]  DEFAULT (getdate()) FOR [ClientRegistrationDateTime]
GO
ALTER TABLE [dbo].[ClientHosts] ADD  CONSTRAINT [DF_ClientHosts_Status]  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[ClientHosts] ADD  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[DeploymentInstructions] ADD  CONSTRAINT [DF_DeploymentInstructions_IsActive]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[DeploymentInstructions] ADD  CONSTRAINT [DF_DeploymentInstructions_CreatedAt]  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[DeploymentJobLogs] ADD  CONSTRAINT [DF_DeploymentJobLogs_LoggedAt]  DEFAULT (getdate()) FOR [LoggedAt]
GO
ALTER TABLE [dbo].[DeploymentJobs] ADD  CONSTRAINT [DF_DeploymentJobs_RequestedAt]  DEFAULT (getdate()) FOR [RequestedAt]
GO
ALTER TABLE [dbo].[DeploymentInstructions]  WITH CHECK ADD  CONSTRAINT [CK_DeploymentInstructions_DefaultConfiguration] CHECK  (([DefaultConfiguration] IS NULL OR isjson([DefaultConfiguration])=(1)))
GO
ALTER TABLE [dbo].[DeploymentInstructions] CHECK CONSTRAINT [CK_DeploymentInstructions_DefaultConfiguration]
GO
ALTER TABLE [dbo].[DeploymentJobs]  WITH CHECK ADD  CONSTRAINT [CK_DeploymentJobs_Configuration] CHECK  (([Configuration] IS NULL OR isjson([Configuration])=(1)))
GO
ALTER TABLE [dbo].[DeploymentJobs] CHECK CONSTRAINT [CK_DeploymentJobs_Configuration]
GO
ALTER TABLE [dbo].[DeploymentJobs]  WITH CHECK ADD  CONSTRAINT [CK_DeploymentJobs_Status] CHECK  (([Status]='Cancelled' OR [Status]='RollbackFailed' OR [Status]='RolledBack' OR [Status]='Failed' OR [Status]='Succeeded' OR [Status]='RollingBack' OR [Status]='Verifying' OR [Status]='Running' OR [Status]='Assigned' OR [Status]='Pending'))
GO
ALTER TABLE [dbo].[DeploymentJobs] CHECK CONSTRAINT [CK_DeploymentJobs_Status]
GO


// Predefined data -- MinIO Deployment Playbook
INSERT [dbo].[DeploymentInstructions] ([ID], [PackageCode], [PackageName], [Version], [Platform], [InstallPlaybookYaml], [VerificationPlaybookYaml], [RollbackPlaybookYaml], [DefaultConfiguration], [IsActive], [CreatedAt]) VALUES (3, N'minio-package', N'MinIO', N'1.0.0', N'Windows', N'---
- name: Install MinIO as a Docker container
  hosts: localhost
  connection: local
  gather_facts: false
  vars:
    ansible_python_interpreter: /usr/bin/python3.12

  tasks:
    - name: Start MinIO
      community.docker.docker_container:
        name: "{{ deployment_config.containerName }}"
        image: "{{ deployment_config.image }}"
        state: started
        restart_policy: unless-stopped
        command:
           - minio
           - server
           - /mnt/data
           - --console-address
           - ":9001"
           - --license
           - /minio.license
        published_ports:
          - "{{ deployment_config.apiPort }}:9000"
          - "{{ deployment_config.consolePort }}:9001"
        volumes:
          - aistor_data:/mnt/data
          - "{{ deployment_config.licenseFilePath }}:/minio.license:ro"
        env:
          MINIO_ROOT_USER: "{{ deployment_config.rootUser }}"
          MINIO_ROOT_PASSWORD: "{{ deployment_config.rootPassword }}"
        pull: true
        recreate: false', N'---
- name: Verify MinIO
  hosts: localhost
  connection: local
  gather_facts: false

  tasks:
    - name: Inspect the MinIO container
      community.docker.docker_container_info:
        name: "{{ deployment_config.containerName }}"
      register: minio_info

    - name: Require the container to exist and be running
      ansible.builtin.assert:
        that:
          - minio_info.exists
          - minio_info.container.State.Running
        fail_msg: "MinIO container is missing or not running."

    - name: Wait for MinIO API readiness
      ansible.builtin.uri:
        url: "http://127.0.0.1:{{ deployment_config.apiPort }}/minio/health/ready"
        method: GET
        status_code: 200
      register: minio_health
      until: minio_health.status | default(0) == 200
      retries: 30
      delay: 2', N'---
- name: Roll back MinIO container deployment
  hosts: localhost
  connection: local
  gather_facts: false

  tasks:
    - name: Remove the MinIO container and preserve its data volume
      community.docker.docker_container:
        name: "{{ deployment_config.containerName }}"
        state: absent
        keep_volumes: true', N'{
    "image": "quay.io/minio/aistor/minio:latest",
    "containerName": "aistor-server",
    "apiPort": 9000,
    "consolePort": 9001,
    "rootUser": "minioadmin",
    "rootPassword": "minioadmin",
    "licenseFilePath": "/home/pang/deployments/minio/minio.license"
  }', 1, CAST(N'2026-10-23T07:30:42.043' AS DateTime))
GO