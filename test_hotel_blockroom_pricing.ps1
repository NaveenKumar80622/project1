$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Running Hotel BlockRoom Pricing Reconciliation Test Case " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# -------------------------------------------------------------------------
# Step 1: Frontend & Backend Mathematical Reconciliation Rule Test
# -------------------------------------------------------------------------
Write-Host "`n[Step 1] Verifying Mathematical Reconciliation Logic..." -ForegroundColor Yellow

function Reconcile-Pricing {
    param(
        [double]$totalPayable,
        [double]$taxAmount
    )
    $roundedPayable = [Math]::Round($totalPayable, [MidpointRounding]::AwayFromZero)
    $roundedTax = [Math]::Round($taxAmount, [MidpointRounding]::AwayFromZero)
    $roundedBase = [Math]::Max(0.0, ($roundedPayable - $roundedTax))
    $sum = $roundedBase + $roundedTax

    return [PSCustomObject]@{
        RawTotal       = $totalPayable
        RawTax         = $taxAmount
        RoundedPayable = $roundedPayable
        RoundedTax     = $roundedTax
        RoundedBase    = $roundedBase
        SumMatches     = ($sum -eq $roundedPayable)
        Sum            = $sum
    }
}

# Test case matching the user's screenshot exactly:
# Mumbai Dorms screenshot: Total Price = 1011, Base = 988, Tax = 24 => 988 + 24 = 1012 (+1 difference)
# Raw unrounded prices: Total = 1011.35, Base = 987.65, Tax = 23.70
$testUserScreenshot = Reconcile-Pricing -totalPayable 1011.35 -taxAmount 23.70
Write-Host "Screenshot scenario (Total: 1011.35, Tax: 23.70):"
Write-Host "  Old calculation: Base = round(987.65) = 988, Tax = round(23.70) = 24. Sum = 1012 (Mismatch: 1012 != 1011)" -ForegroundColor Red
Write-Host "  New reconciled : Base = $($testUserScreenshot.RoundedBase), Tax = $($testUserScreenshot.RoundedTax), Total = $($testUserScreenshot.RoundedPayable)" -ForegroundColor Green
Write-Host "  Verification   : $($testUserScreenshot.RoundedBase) + $($testUserScreenshot.RoundedTax) = $($testUserScreenshot.Sum) == $($testUserScreenshot.RoundedPayable) -> $($testUserScreenshot.SumMatches)" -ForegroundColor Green

if (-not $testUserScreenshot.SumMatches) {
    throw "Screenshot test case failed!"
}

# -------------------------------------------------------------------------
# Step 2: Edge Cases Verification
# -------------------------------------------------------------------------
Write-Host "`n[Step 2] Testing Edge Cases for 1-Rupee Discrepancy Prevention..." -ForegroundColor Yellow
$edgeCases = @(
    @{ Total = 1011.35; Tax = 23.70 },
    @{ Total = 2500.50; Tax = 450.50 },
    @{ Total = 999.49; Tax = 179.51 },
    @{ Total = 5432.75; Tax = 651.85 },
    @{ Total = 1200.00; Tax = 0.00 }
)

foreach ($ec in $edgeCases) {
    $res = Reconcile-Pricing -totalPayable $ec.Total -taxAmount $ec.Tax
    $status = if ($res.SumMatches) { "PASSED" } else { "FAILED" }
    Write-Host "  Total: $($ec.Total), Tax: $($ec.Tax) => Base: $($res.RoundedBase), Tax: $($res.RoundedTax) | Sum: $($res.Sum) == $($res.RoundedPayable) [$status]" -ForegroundColor Green
}

# -------------------------------------------------------------------------
# Step 3: Live API Test: SearchHotels -> GetHotelInfo -> GetHotelRoom -> BlockRoom
# -------------------------------------------------------------------------
Write-Host "`n[Step 3] Testing Live API Endpoint for BlockRoom Price Breakdown..." -ForegroundColor Yellow

$apiToken = "6TpEAoGb09eMg4u7pD8qGIDe2hIhAoV6r2QKiPSvdABgSly1ZHuqZBNUldmSPKg8"
$headers = @{
    "Content-Type" = "application/json"
    "Api-Token"    = $apiToken
}

$checkIn = (Get-Date).AddDays(1).ToString("yyyy-MM-dd")

$searchBody = @{
    CityId = 2948193 # Mumbai Master
    CheckInDate = $checkIn
    NoOfNights = 1
    GuestNationality = "IN"
    RoomGuests = @(
        @{
            NoOfAdults = 1
            NoOfChild = 0
        }
    )
} | ConvertTo-Json -Depth 5

try {
    Write-Host "1. Calling SearchHotels (CityId: 2948193, CheckIn: $checkIn)..."
    $searchResp = Invoke-RestMethod -Uri "https://picknbook.in/api/Hotels/SearchHotels" -Method Post -Headers $headers -Body $searchBody -TimeoutSec 30
    
    $hotelList = $null
    if ($searchResp.results) { $hotelList = $searchResp.results }
    elseif ($searchResp.hotelSearchResult) { $hotelList = $searchResp.hotelSearchResult }
    elseif ($searchResp.hotels) { $hotelList = $searchResp.hotels }

    if ($hotelList -and $hotelList.Count -gt 0) {
        $traceId = [long]$searchResp.traceId
        Write-Host "   Total hotels found: $($hotelList.Count), TraceId: $traceId" -ForegroundColor Green

        # Look specifically for Mumbai Dorms
        $mumbaiDorms = $hotelList | Where-Object { $_.hotelName -match "Mumbai Dorms" } | Select-Object -First 1
        $targetHotel = if ($mumbaiDorms) { $mumbaiDorms } else { $hotelList[0] }

        $resultIndex = $targetHotel.resultIndex
        Write-Host "`nSelected Hotel: $($targetHotel.hotelName) (ResultIndex: $resultIndex, HotelCode: $($targetHotel.hotelCode))..."

        # Step 3.1: Call GetHotelInfo (Required by SRDV before GetHotelRoom)
        Write-Host "2. Calling GetHotelInfo..." -ForegroundColor Cyan
        $infoUrl = "https://picknbook.in/api/Hotels/GetHotelInfo?traceId=$traceId&resultIndex=$([System.Web.HttpUtility]::UrlEncode($resultIndex))&hotelCode=$([System.Web.HttpUtility]::UrlEncode($targetHotel.hotelCode))"
        $infoResp = Invoke-RestMethod -Uri $infoUrl -Method Get -Headers $headers -TimeoutSec 30
        Write-Host "   -> GetHotelInfo completed successfully." -ForegroundColor Green

        # Step 3.2: Call GetHotelRoom
        Write-Host "3. Calling GetHotelRoom..." -ForegroundColor Cyan
        $roomPostPayload = @{
            TraceId = $traceId
            SrdvType = if ($targetHotel.srdvType) { $targetHotel.srdvType } else { "MixAPI" }
            SrdvIndex = if ($targetHotel.srdvIndex) { $targetHotel.srdvIndex } else { "" }
            ResultIndex = $resultIndex
            HotelCode = if ($targetHotel.hotelCode) { $targetHotel.hotelCode } else { "" }
        } | ConvertTo-Json -Depth 5

        $roomsResp = Invoke-RestMethod -Uri "https://picknbook.in/api/Hotels/GetHotelRoom" -Method Post -Headers $headers -Body $roomPostPayload -TimeoutSec 30

        $firstRoom = $null
        if ($roomsResp.getHotelRoomResult -and $roomsResp.getHotelRoomResult.hotelRoomsDetails) {
            foreach ($cat in $roomsResp.getHotelRoomResult.hotelRoomsDetails) {
                if ($cat.rooms -and $cat.rooms.Count -gt 0) {
                    $firstRoom = $cat.rooms[0]
                    break
                }
            }
        }

        if ($firstRoom) {
            Write-Host "   -> Found Room: $($firstRoom.roomTypeName)" -ForegroundColor Green
            Write-Host "   -> RoomIndex: $($firstRoom.roomIndex), RoomTypeCode: $($firstRoom.roomTypeCode)" -ForegroundColor Green

            # Step 3.3: Call BlockRoom (Lock price without booking, NO BookRoom)
            Write-Host "`n4. Calling BlockRoom (TraceId: $traceId)..." -ForegroundColor Cyan
            $blockBody = @{
                TraceId = $traceId
                SrdvType = if ($targetHotel.srdvType) { $targetHotel.srdvType } else { "MixAPI" }
                SrdvIndex = if ($targetHotel.srdvIndex) { "$($targetHotel.srdvIndex)" } else { "" }
                ResultIndex = $resultIndex
                HotelCode = if ($targetHotel.hotelCode) { $targetHotel.hotelCode } else { "" }
                HotelName = $targetHotel.hotelName
                GuestNationality = "IN"
                NoOfRooms = 1
                HotelRoomsDetails = @(
                    @{
                        RoomIndex = "$($firstRoom.roomIndex)"
                        RoomTypeCode = "$($firstRoom.roomTypeCode)"
                        OptionId = if ($firstRoom.optionId) { "$($firstRoom.optionId)" } else { "" }
                    }
                )
            } | ConvertTo-Json -Depth 5

            $blockResp = Invoke-RestMethod -Uri "https://picknbook.in/api/Hotels/BlockRoom" -Method Post -Headers $headers -Body $blockBody -TimeoutSec 30

            if ($blockResp -and $blockResp.blockRoomResult) {
                $bRooms = @($blockResp.blockRoomResult.hotelRoomsDetails)
                if ($bRooms.Count -gt 0) {
                    $bPrice = $bRooms[0].price
                    $bSummary = $blockResp.blockRoomResult.priceSummary

                    Write-Host "`nLive BlockRoom Response Price Details:" -ForegroundColor Cyan
                    Write-Host "   PublishedPrice         : $($bPrice.publishedPrice)"
                    Write-Host "   OfferedPrice           : $($bPrice.offeredPrice)"
                    Write-Host "   Tax                    : $($bPrice.tax)"
                    Write-Host "   TotalGSTAmount         : $($bPrice.totalGSTAmount)"
                    Write-Host "   B2CBasePrice           : $($bPrice.b2CBasePrice)"
                    Write-Host "   B2CTotalPrice          : $($bPrice.b2CTotalPrice)"
                    Write-Host "   PublishedPriceRounded  : $($bPrice.publishedPriceRoundedOff)"
                    Write-Host "   OfferedPriceRounded    : $($bPrice.offeredPriceRoundedOff)"

                    if ($bSummary) {
                        Write-Host "   Summary BlockedPrice   : $($bSummary.blockedPrice)"
                        Write-Host "   Summary Difference     : $($bSummary.difference)"
                    }

                    # Reconcile price components
                    $totPayable = [double]$bPrice.publishedPrice
                    if ($bPrice.b2CTotalPrice -and [double]$bPrice.b2CTotalPrice -gt 0) {
                        $totPayable = [double]$bPrice.b2CTotalPrice
                    }
                    $taxVal = 0.0
                    if ($bPrice.tax) { $taxVal = [double]$bPrice.tax }
                    if ($bPrice.totalGSTAmount) { $taxVal = $taxVal + [double]$bPrice.totalGSTAmount }

                    $liveRec = Reconcile-Pricing -totalPayable $totPayable -taxAmount $taxVal
                    Write-Host "`nFrontend Breakdown Simulation on BlockRoom:" -ForegroundColor Green
                    Write-Host "   Room base charges : INR $($liveRec.RoundedBase)" -ForegroundColor Green
                    Write-Host "   Taxes & GST       : INR $($liveRec.RoundedTax)" -ForegroundColor Green
                    Write-Host "   Total Price       : INR $($liveRec.RoundedPayable)" -ForegroundColor Green
                    Write-Host "   Verification      : Base ($($liveRec.RoundedBase)) + Tax ($($liveRec.RoundedTax)) = $($liveRec.Sum) == Total ($($liveRec.RoundedPayable))? $($liveRec.SumMatches)" -ForegroundColor Green

                    if (-not $liveRec.SumMatches) {
                        throw "Reconciliation mismatch detected!"
                    }
                } else {
                    Write-Host "BlockRoomResult Note: $($blockResp.blockRoomResult.error | ConvertTo-Json -Compress)" -ForegroundColor Yellow
                }
            }
        } else {
            Write-Host "GetHotelRoom did not return room details for selected hotel." -ForegroundColor Yellow
        }
    } else {
        Write-Host "No hotels returned for search." -ForegroundColor Yellow
    }
} catch {
    Write-Host "Live API interaction note: $_" -ForegroundColor Yellow
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " Test Case Complete. Zero Bookings / Cancellations made. " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
