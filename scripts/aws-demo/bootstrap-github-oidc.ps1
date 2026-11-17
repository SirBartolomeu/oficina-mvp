param(
    [string]$AccountId = '879310855568',
    [string]$GitHubOwner = 'SirBartolomeu',
    [string]$GitHubOwnerId = '75947038',
    [string]$DataRepoId = '1460805172',
    [string]$K3sRepoId = '1460805236',
    [string]$StateBucket = 'oficina-fiap-demo-tfstate-879310855568-us-east-1',
    [string]$VpcId = 'vpc-0c8195fad67689235',
    [string]$AuthApiId = 'un67q1d3jk'
)

$ErrorActionPreference = 'Stop'
$region = 'us-east-1'
$prefix = 'oficina-fiap-demo'
$identity = aws sts get-caller-identity --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $identity.Account -ne $AccountId) { throw 'AWS account mismatch.' }
if ($identity.Arn -ne "arn:aws:iam::$($AccountId):root") {
    throw 'Run bootstrap only from the confirmed root session.'
}
$existingVpc = aws ec2 describe-vpcs --region $region --vpc-ids $VpcId --query 'Vpcs[0].VpcId' --output text
if ($LASTEXITCODE -ne 0 -or $existingVpc -ne $VpcId) { throw 'The configured K3s VPC does not exist in the selected region.' }

$provider = aws iam list-open-id-connect-providers --query "OpenIDConnectProviderList[?ends_with(Arn, 'token.actions.githubusercontent.com')].Arn | [0]" --output text
if ($LASTEXITCODE -ne 0) { throw 'Unable to list OIDC providers.' }
if (-not $provider -or $provider -eq 'None') {
    $provider = aws iam create-open-id-connect-provider --url https://token.actions.githubusercontent.com --client-id-list sts.amazonaws.com --query OpenIDConnectProviderArn --output text
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the GitHub OIDC provider.' }
}

$repos = @{
    Data = $GitHubOwner + '/oficina-data-infra'
    K3s = $GitHubOwner + '/oficina-k8s-infra'
}
$dataActions = @(
    'ec2:Describe*','ec2:CreateVpc','ec2:DeleteVpc','ec2:ModifyVpcAttribute','ec2:CreateSubnet','ec2:DeleteSubnet','ec2:ModifySubnetAttribute',
    'ec2:CreateInternetGateway','ec2:AttachInternetGateway','ec2:DetachInternetGateway','ec2:DeleteInternetGateway','ec2:CreateRouteTable','ec2:DeleteRouteTable',
    'ec2:CreateRoute','ec2:DeleteRoute','ec2:AssociateRouteTable','ec2:DisassociateRouteTable','ec2:CreateSecurityGroup','ec2:DeleteSecurityGroup',
    'ec2:AuthorizeSecurityGroupIngress','ec2:AuthorizeSecurityGroupEgress','ec2:RevokeSecurityGroupIngress','ec2:RevokeSecurityGroupEgress','ec2:CreateTags','ec2:DeleteTags',
    'rds:CreateDBSubnetGroup','rds:DeleteDBSubnetGroup','rds:DescribeDBInstances','rds:DescribeDBSubnetGroups','rds:ListTagsForResource','rds:AddTagsToResource','rds:RemoveTagsFromResource',
    'rds:CreateDBInstance','rds:ModifyDBInstance','rds:DeleteDBInstance',
    'kms:DescribeKey',
    'dynamodb:CreateTable','dynamodb:DescribeTable','dynamodb:DescribeContinuousBackups','dynamodb:DescribeTimeToLive','dynamodb:UpdateTable','dynamodb:DeleteTable','dynamodb:TagResource','dynamodb:UntagResource','dynamodb:ListTagsOfResource',
    'sqs:CreateQueue','sqs:DeleteQueue','sqs:GetQueueAttributes','sqs:SetQueueAttributes','sqs:TagQueue','sqs:UntagQueue','sqs:ListQueueTags',
    'ecr:CreateRepository','ecr:DeleteRepository','ecr:DescribeRepositories','ecr:ListTagsForResource','ecr:PutLifecyclePolicy','ecr:GetLifecyclePolicy','ecr:DeleteLifecyclePolicy','ecr:TagResource','ecr:UntagResource',
    'secretsmanager:CreateSecret','secretsmanager:DeleteSecret','secretsmanager:DescribeSecret','secretsmanager:GetResourcePolicy','secretsmanager:TagResource','secretsmanager:UntagResource',
    's3:GetBucketLocation','s3:ListBucket','s3:GetObject','s3:PutObject','s3:DeleteObject'
)
$k3sActions = @(
    'ec2:Describe*','ec2:RunInstances','ec2:TerminateInstances','ec2:CreateTags','ec2:DeleteTags','ec2:CreateSecurityGroup','ec2:DeleteSecurityGroup',
    'ec2:AuthorizeSecurityGroupIngress','ec2:AuthorizeSecurityGroupEgress','ec2:RevokeSecurityGroupIngress','ec2:RevokeSecurityGroupEgress',
    'ec2:CreateVpcEndpoint','ec2:DeleteVpcEndpoints','ec2:ModifyVpcEndpoint','ec2:CreateNetworkInterface','ec2:DeleteNetworkInterface','ec2:GetSecurityGroupsForVpc',
    'ec2:ModifyInstanceAttribute','ec2:ModifyInstanceMetadataOptions','ec2:ModifyVolume','ec2:StartInstances','ec2:StopInstances',
    'ec2:AssociateIamInstanceProfile','ec2:DisassociateIamInstanceProfile','ec2:ReplaceIamInstanceProfileAssociation','ec2:ModifySecurityGroupRules',
    'ssm:GetParameter','iam:CreateRole','iam:DeleteRole','iam:GetRole','iam:UpdateRole','iam:UpdateAssumeRolePolicy','iam:TagRole','iam:UntagRole',
    'iam:ListRolePolicies','iam:ListAttachedRolePolicies','iam:ListOpenIDConnectProviders','iam:GetOpenIDConnectProvider','iam:PutRolePolicy','iam:GetRolePolicy','iam:DeleteRolePolicy','iam:AttachRolePolicy','iam:DetachRolePolicy',
    'iam:CreateInstanceProfile','iam:DeleteInstanceProfile','iam:GetInstanceProfile','iam:AddRoleToInstanceProfile','iam:RemoveRoleFromInstanceProfile','iam:TagInstanceProfile','iam:UntagInstanceProfile','iam:PassRole',
    'lambda:CreateFunction','lambda:UpdateFunctionCode','lambda:UpdateFunctionConfiguration','lambda:DeleteFunction','lambda:GetFunction','lambda:GetFunctionConfiguration','lambda:GetFunctionCodeSigningConfig','lambda:GetPolicy','lambda:ListTags','lambda:ListVersionsByFunction',
    'lambda:AddPermission','lambda:RemovePermission','lambda:TagResource','lambda:UntagResource',
    'apigateway:GET','apigateway:POST','apigateway:PUT','apigateway:PATCH','apigateway:DELETE','apigateway:TagResource',
    'ssm:CreateDocument','ssm:UpdateDocument','ssm:UpdateDocumentDefaultVersion','ssm:DeleteDocument','ssm:DescribeDocument','ssm:DescribeDocumentPermission','ssm:GetDocument','ssm:AddTagsToResource','ssm:RemoveTagsFromResource',
    's3:CreateBucket','s3:DeleteBucket','s3:GetBucketLocation','s3:ListBucket','s3:GetBucketPolicy','s3:GetBucketAcl','s3:GetBucketCORS',
    's3:GetBucketWebsite','s3:GetBucketVersioning','s3:GetAccelerateConfiguration','s3:GetBucketRequestPayment','s3:GetBucketLogging',
    's3:GetReplicationConfiguration','s3:GetBucketObjectLockConfiguration','s3:GetBucketOwnershipControls','s3:GetBucketPolicyStatus',
    's3:GetBucketTagging','s3:PutBucketTagging',
    's3:GetBucketPublicAccessBlock','s3:PutBucketPublicAccessBlock','s3:GetEncryptionConfiguration','s3:PutEncryptionConfiguration',
    's3:GetLifecycleConfiguration','s3:PutLifecycleConfiguration',
    's3:PutObject','s3:GetObject','s3:DeleteObject','s3:ListBucketMultipartUploads','s3:AbortMultipartUpload',
    'logs:CreateLogGroup','logs:DeleteLogGroup','logs:DescribeLogGroups','logs:PutRetentionPolicy','logs:DeleteRetentionPolicy',
    'logs:ListTagsForResource','logs:TagResource','logs:UntagResource'
)
$stateArn = 'arn:aws:s3:::' + $StateBucket
$secretsManagerKmsKeyArn = aws kms describe-key --region $region --key-id alias/aws/secretsmanager --query KeyMetadata.Arn --output text
if ($LASTEXITCODE -ne 0 -or -not $secretsManagerKmsKeyArn -or $secretsManagerKmsKeyArn -eq 'None') { throw 'Unable to resolve the Secrets Manager AWS-managed KMS key.' }
$dataResources = @('*', $stateArn, ($stateArn + '/*'))
$k3sResources = @(
    '*', $stateArn, ($stateArn + '/*'),
    ('arn:aws:iam::' + $AccountId + ':role/' + $prefix + '-*'),
    ('arn:aws:iam::' + $AccountId + ':instance-profile/' + $prefix + '-*'),
    ('arn:aws:lambda:' + $region + ':' + $AccountId + ':function:' + $prefix + '-*'),
    ('arn:aws:ssm:' + $region + ':' + $AccountId + ':document/' + $prefix + '-*'),
    ('arn:aws:s3:::' + $prefix + '-assets-' + $AccountId + '-' + $region),
    ('arn:aws:s3:::' + $prefix + '-assets-' + $AccountId + '-' + $region + '/*')
)

function Set-OidcRole([string]$Name, [string]$Repo, [string]$RepoId, [string[]]$Actions, [string[]]$Resources) {
    $repoParts = $Repo.Split('/', 2)
    $immutableRepo = $repoParts[0] + '@' + $GitHubOwnerId + '/' + $repoParts[1] + '@' + $RepoId
    $mainSubject = 'repo:' + $immutableRepo + ':ref:refs/heads/main'
    $environmentSubject = 'repo:' + $immutableRepo + ':environment:aws-demo'
    $trust = @{
        Version = '2012-10-17'
        Statement = @(@{
            Effect = 'Allow'
            Principal = @{ Federated = $provider }
            Action = 'sts:AssumeRoleWithWebIdentity'
            Condition = @{ StringEquals = @{
                'token.actions.githubusercontent.com:aud' = 'sts.amazonaws.com'
                'token.actions.githubusercontent.com:sub' = @($mainSubject, $environmentSubject)
            } }
        })
    } | ConvertTo-Json -Depth 10
    $statementGroups = $Actions | Group-Object { $_.Split(':')[0] }
    $statements = foreach ($group in $statementGroups) {
        if ($group.Name -eq 's3') {
            $stateActions = @($group.Group | Where-Object {
                $_ -in @('s3:GetBucketLocation','s3:ListBucket','s3:GetObject','s3:PutObject','s3:DeleteObject')
            })
            $stateResources = @($stateArn, ($stateArn + '/*'))
            if ($stateActions.Count -gt 0) {
                [ordered]@{ Effect = 'Allow'; Action = $stateActions; Resource = $stateResources }
            }
            $assetActions = @($group.Group | Where-Object { $_ -notin $stateActions })
            if ($Name -eq ($prefix + '-infra-k3s')) {
                $assetActions = @($group.Group)
            }
            if ($assetActions.Count -gt 0) {
                $assetArn = 'arn:aws:s3:::' + $prefix + '-assets-' + $AccountId + '-' + $region
                [ordered]@{ Effect = 'Allow'; Action = $assetActions; Resource = @($assetArn, ($assetArn + '/*')) }
            }
            continue
        }
        if ($group.Name -eq 'secretsmanager') {
            $rdsSecretActions = @($group.Group | Where-Object { $_ -in @('secretsmanager:CreateSecret','secretsmanager:TagResource') })
            $appSecretActions = @($group.Group | Where-Object { $_ -notin $rdsSecretActions })
            $appSecretArn = 'arn:aws:secretsmanager:' + $region + ':' + $AccountId + ':secret:' + $prefix + '/*'
            if ($appSecretActions.Count -gt 0) {
                [ordered]@{ Effect = 'Allow'; Action = $appSecretActions; Resource = @($appSecretArn) }
            }
            if ($rdsSecretActions.Count -gt 0) {
                $rdsManagedSecretArn = 'arn:aws:secretsmanager:' + $region + ':' + $AccountId + ':secret:rds!db-*'
                [ordered]@{ Effect = 'Allow'; Action = $rdsSecretActions; Resource = @($rdsManagedSecretArn) }
            }
            continue
        }
        if ($group.Name -eq 'ec2') {
            $describeActions = @($group.Group | Where-Object { $_ -eq 'ec2:Describe*' })
            $writeActions = @($group.Group | Where-Object { $_ -ne 'ec2:Describe*' })
            $authorizeRuleActions = @()
            if ($Name -eq ($prefix + '-infra-k3s')) {
                $authorizeRuleActions = @($writeActions | Where-Object { $_ -in @('ec2:AuthorizeSecurityGroupIngress','ec2:AuthorizeSecurityGroupEgress') })
                $writeActions = @($writeActions | Where-Object { $_ -notin $authorizeRuleActions })
            }
            if ($describeActions.Count -gt 0) {
                [ordered]@{ Effect = 'Allow'; Action = $describeActions; Resource = '*' }
            }
            foreach ($action in $writeActions) {
                $resourceTypes = switch ($action) {
                    { $_ -in @('ec2:CreateVpc','ec2:DeleteVpc','ec2:ModifyVpcAttribute','ec2:GetSecurityGroupsForVpc') } { @('vpc'); break }
                    { $_ -in @('ec2:CreateSubnet','ec2:DeleteSubnet','ec2:ModifySubnetAttribute') } { @('subnet'); break }
                    { $_ -in @('ec2:CreateInternetGateway','ec2:DeleteInternetGateway') } { @('internet-gateway'); break }
                    { $_ -in @('ec2:AttachInternetGateway','ec2:DetachInternetGateway') } { @('internet-gateway','vpc'); break }
                    { $_ -in @('ec2:CreateRouteTable','ec2:DeleteRouteTable','ec2:CreateRoute','ec2:DeleteRoute') } { @('route-table'); break }
                    { $_ -in @('ec2:AssociateRouteTable','ec2:DisassociateRouteTable') } { @('route-table','subnet'); break }
                    'ec2:CreateSecurityGroup' {
                        if ($Name -eq ($prefix + '-infra-k3s')) { @('security-group', ('vpc/' + $VpcId)) }
                        else { @('security-group','vpc') }
                        break
                    }
                    { $_ -in @('ec2:DeleteSecurityGroup','ec2:AuthorizeSecurityGroupIngress','ec2:AuthorizeSecurityGroupEgress','ec2:RevokeSecurityGroupIngress','ec2:RevokeSecurityGroupEgress') } { @('security-group'); break }
                    { $_ -in @('ec2:CreateTags','ec2:DeleteTags') } { @('vpc','subnet','internet-gateway','route-table','security-group','security-group-rule','vpc-endpoint','network-interface','instance','volume'); break }
                    { $_ -in @('ec2:RunInstances') } { @('instance','volume','network-interface','subnet','security-group','key-pair') + @('image'); break }
                    'ec2:TerminateInstances' { @('instance'); break }
                    { $_ -in @('ec2:ModifyInstanceAttribute','ec2:ModifyInstanceMetadataOptions','ec2:StartInstances','ec2:StopInstances','ec2:AssociateIamInstanceProfile','ec2:DisassociateIamInstanceProfile','ec2:ReplaceIamInstanceProfileAssociation') } { @('instance'); break }
                    'ec2:ModifyVolume' { @('volume'); break }
                    'ec2:ModifySecurityGroupRules' { @('security-group','security-group-rule'); break }
                    { $_ -in @('ec2:CreateVpcEndpoint','ec2:DeleteVpcEndpoints','ec2:ModifyVpcEndpoint') } { @('vpc-endpoint','vpc','subnet','route-table','security-group'); break }
                    { $_ -in @('ec2:CreateNetworkInterface','ec2:DeleteNetworkInterface') } { @('network-interface'); break }
                    default { throw ('No EC2 resource mapping for ' + $action) }
                }
                $ec2Arns = @($resourceTypes | ForEach-Object {
                    if ($_ -eq 'image') { 'arn:aws:ec2:' + $region + '::image/*' }
                    elseif ($_ -like 'vpc/*') { 'arn:aws:ec2:' + $region + ':' + $AccountId + ':' + $_ }
                    else { 'arn:aws:ec2:' + $region + ':' + $AccountId + ':' + $_ + '/*' }
                })
                [ordered]@{ Effect = 'Allow'; Action = @($action); Resource = $ec2Arns }
            }
            if ($authorizeRuleActions.Count -gt 0) {
                $vpcArn = 'arn:aws:ec2:' + $region + ':' + $AccountId + ':vpc/' + $VpcId
                [ordered]@{
                    Effect = 'Allow'; Action = $authorizeRuleActions
                    Resource = @('arn:aws:ec2:' + $region + ':' + $AccountId + ':security-group/*')
                    Condition = @{ ArnEquals = @{ 'ec2:Vpc' = $vpcArn } }
                }
                [ordered]@{
                    Effect = 'Allow'; Action = $authorizeRuleActions
                    Resource = @('arn:aws:ec2:' + $region + ':' + $AccountId + ':security-group-rule/*')
                    Condition = @{
                        StringEquals = @{
                            'aws:RequestTag/Project' = 'oficina-fiap'
                            'aws:RequestTag/Environment' = 'demo'
                            'aws:RequestTag/ManagedBy' = 'terraform'
                        }
                        'ForAllValues:StringEquals' = @{ 'aws:TagKeys' = @('Project','Environment','ManagedBy','Name') }
                    }
                }
            }
            continue
        }
        if ($group.Name -eq 'rds') {
            $describeAllInstances = @($group.Group | Where-Object { $_ -eq 'rds:DescribeDBInstances' })
            $scopedRdsActions = @($group.Group | Where-Object { $_ -ne 'rds:DescribeDBInstances' })
            $rdsResourceArns = @(
                ('arn:aws:rds:' + $region + ':' + $AccountId + ':db:' + $prefix + '-*'),
                ('arn:aws:rds:' + $region + ':' + $AccountId + ':subgrp:' + $prefix + '-*')
            )
            if ($describeAllInstances.Count -gt 0) {
                $allDbArn = 'arn:aws:rds:' + $region + ':' + $AccountId + ':db:*'
                [ordered]@{ Effect = 'Allow'; Action = $describeAllInstances; Resource = @($allDbArn) }
            }
            if ($scopedRdsActions.Count -gt 0) {
                [ordered]@{ Effect = 'Allow'; Action = $scopedRdsActions; Resource = $rdsResourceArns }
            }
            continue
        }
        if ($group.Name -eq 'logs') {
            $listGroupsActions = @($group.Group | Where-Object { $_ -eq 'logs:DescribeLogGroups' })
            $scopedLogsActions = @($group.Group | Where-Object { $_ -ne 'logs:DescribeLogGroups' })
            if ($listGroupsActions.Count -gt 0) {
                [ordered]@{
                    Effect = 'Allow'; Action = $listGroupsActions; Resource = '*'
                    Condition = @{ StringEquals = @{ 'aws:RequestedRegion' = $region } }
                }
            }
            if ($scopedLogsActions.Count -gt 0) {
                $logGroupArn = 'arn:aws:logs:' + $region + ':' + $AccountId + ':log-group:/aws/lambda/' + $prefix + '-*'
                [ordered]@{ Effect = 'Allow'; Action = $scopedLogsActions; Resource = @($logGroupArn) }
            }
            continue
        }
        $serviceResources = switch ($group.Name) {
            'kms' { @($secretsManagerKmsKeyArn); break }
            'dynamodb' { @(('arn:aws:dynamodb:' + $region + ':' + $AccountId + ':table/' + $prefix + '-*')); break }
            'sqs' { @(('arn:aws:sqs:' + $region + ':' + $AccountId + ':' + $prefix + '-*')); break }
            'ecr' { @(('arn:aws:ecr:' + $region + ':' + $AccountId + ':repository/' + $prefix + '-*')); break }
            'lambda' { @(('arn:aws:lambda:' + $region + ':' + $AccountId + ':function:' + $prefix + '-*')); break }
            'iam' { @(('arn:aws:iam::' + $AccountId + ':role/' + $prefix + '-*'), ('arn:aws:iam::' + $AccountId + ':instance-profile/' + $prefix + '-*')); break }
            'ssm' { @(
                ('arn:aws:ssm:' + $region + ':' + $AccountId + ':document/' + $prefix + '-*'),
                ('arn:aws:ssm:' + $region + '::parameter/aws/service/ami-*')
            ); break }
            'apigateway' { @(
                ('arn:aws:apigateway:' + $region + '::/apis'),
                ('arn:aws:apigateway:' + $region + '::/apis/??????????'),
                ('arn:aws:apigateway:' + $region + '::/apis/??????????/integrations'),
                ('arn:aws:apigateway:' + $region + '::/apis/??????????/integrations/*'),
                ('arn:aws:apigateway:' + $region + '::/apis/??????????/routes'),
                ('arn:aws:apigateway:' + $region + '::/apis/??????????/routes/*'),
                ('arn:aws:apigateway:' + $region + '::/apis/' + $AuthApiId + '/stages'),
                ('arn:aws:apigateway:' + $region + '::/apis/' + $AuthApiId + '/stages/*'),
                ('arn:aws:apigateway:' + $region + '::/tags/*')
            ); break }
            default { @('*') }
        }
        $groupActions = @($group.Group)
        if ($group.Name -eq 'apigateway' -and $Name -eq ($prefix + '-infra-k3s')) {
            $tagActions = @($groupActions | Where-Object { $_ -eq 'apigateway:TagResource' })
            $apiActions = @($groupActions | Where-Object { $_ -ne 'apigateway:TagResource' })
            if ($apiActions.Count -gt 0) {
                [ordered]@{ Effect = 'Allow'; Action = $apiActions; Resource = $serviceResources }
            }
            if ($tagActions.Count -gt 0) {
                [ordered]@{
                    Effect = 'Allow'; Action = $tagActions
                    Resource = @('arn:aws:apigateway:' + $region + '::/apis/' + $AuthApiId + '/stages')
                }
            }
            continue
        }
        if ($group.Name -eq 'iam' -and $Name -eq ($prefix + '-infra-k3s')) {
            # IAM ListOpenIDConnectProviders is account-scoped and requires Resource "*".
            $oidcProviderListActions = @($groupActions | Where-Object { $_ -eq 'iam:ListOpenIDConnectProviders' })
            $oidcProviderReadActions = @($groupActions | Where-Object { $_ -eq 'iam:GetOpenIDConnectProvider' })
            $groupActions = @($groupActions | Where-Object { $_ -ne 'iam:ListOpenIDConnectProviders' -and $_ -ne 'iam:GetOpenIDConnectProvider' })
            $attachmentActions = @($groupActions | Where-Object { $_ -in @('iam:AttachRolePolicy','iam:DetachRolePolicy') })
            $passRoleActions = @($groupActions | Where-Object { $_ -eq 'iam:PassRole' })
            $groupActions = @($groupActions | Where-Object { $_ -notin $attachmentActions -and $_ -notin $passRoleActions })
            if ($groupActions.Count -gt 0) {
                [ordered]@{ Effect = 'Allow'; Action = $groupActions; Resource = $serviceResources }
            }
            if ($oidcProviderListActions.Count -gt 0) {
                [ordered]@{ Effect = 'Allow'; Action = $oidcProviderListActions; Resource = '*' }
            }
            if ($oidcProviderReadActions.Count -gt 0) {
                $oidcProviderArn = 'arn:aws:iam::' + $AccountId + ':oidc-provider/token.actions.githubusercontent.com'
                [ordered]@{ Effect = 'Allow'; Action = $oidcProviderReadActions; Resource = $oidcProviderArn }
            }
            if ($attachmentActions.Count -gt 0) {
                $roleArn = 'arn:aws:iam::' + $AccountId + ':role/' + $prefix + '-*'
                [ordered]@{
                    Effect = 'Allow'; Action = $attachmentActions; Resource = @($roleArn)
                    Condition = @{ StringEquals = @{ 'iam:PolicyARN' = 'arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore' } }
                }
            }
            if ($passRoleActions.Count -gt 0) {
                $passRoleArns = @(
                    ('arn:aws:iam::' + $AccountId + ':role/' + $prefix + '-k3s-host'),
                    ('arn:aws:iam::' + $AccountId + ':role/' + $prefix + '-auth-lambda')
                )
                [ordered]@{
                    Effect = 'Allow'; Action = $passRoleActions; Resource = $passRoleArns
                    Condition = @{ StringEquals = @{ 'iam:PassedToService' = @('ec2.amazonaws.com','lambda.amazonaws.com') } }
                }
                [ordered]@{
                    Effect = 'Allow'; Action = $passRoleActions
                    Resource = @('arn:aws:iam::' + $AccountId + ':role/' + $prefix + '-webhook-forwarder')
                    Condition = @{ StringEquals = @{ 'iam:PassedToService' = 'lambda.amazonaws.com' } }
                }
            }
            continue
        }
        if ($groupActions.Count -gt 0) {
            [ordered]@{ Effect = 'Allow'; Action = $groupActions; Resource = $serviceResources }
        }
    }
    $policy = @{ Version = '2012-10-17'; Statement = @($statements) } | ConvertTo-Json -Depth 10
    $trustPath = Join-Path $env:TEMP ($Name + '-trust.json')
    $policyPath = Join-Path $env:TEMP ($Name + '-policy.json')
    Set-Content -LiteralPath $trustPath -Value $trust -Encoding utf8
    Set-Content -LiteralPath $policyPath -Value $policy -Encoding utf8
    try {
        aws iam get-role --role-name $Name --query Role.RoleName --output text 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            aws iam create-role --role-name $Name --description ('GitHub OIDC: ' + $Repo) --assume-role-policy-document ('file://' + $trustPath) --tags Key=Project,Value=oficina-fiap Key=ManagedBy,Value=bootstrap | Out-Null
            if ($LASTEXITCODE -ne 0) { throw ('Unable to create role ' + $Name) }
        } else {
            aws iam update-assume-role-policy --role-name $Name --policy-document ('file://' + $trustPath)
            if ($LASTEXITCODE -ne 0) { throw ('Unable to update trust for ' + $Name) }
        }
        aws iam put-role-policy --role-name $Name --policy-name ($prefix + '-terraform') --policy-document ('file://' + $policyPath)
        if ($LASTEXITCODE -ne 0) { throw ('Unable to attach policy to ' + $Name) }
        aws iam get-role --role-name $Name --query Role.Arn --output text
        if ($LASTEXITCODE -ne 0) { throw ('Unable to verify role ' + $Name) }
    } finally {
        Remove-Item -LiteralPath $trustPath, $policyPath -Force -ErrorAction SilentlyContinue
    }
}

$dataArn = Set-OidcRole ($prefix + '-infra-data') $repos.Data $DataRepoId $dataActions $dataResources
$k3sArn = Set-OidcRole ($prefix + '-infra-k3s') $repos.K3s $K3sRepoId $k3sActions $k3sResources
Write-Output ('GitHub OIDC provider: ' + $provider)
Write-Output ('Data Terraform role: ' + $dataArn)
Write-Output ('K3s Terraform role: ' + $k3sArn)
Write-Output ('Reserved state bucket name (not created): ' + $StateBucket)
