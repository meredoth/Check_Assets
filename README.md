# Check_Assets

Use this tool to determine whether your account contains any assets published by companies identified as Chinese publishers.

## Setup

* Place the script inside an Editor folder within your Assets directory.

* Place the CSV file directly inside the Assets folder.

## How It Works

After importing the script, a new menu item called “My Asset Store Library” will appear under the Window menu.

When you open it, a window with a single button will be displayed:

<img width="727" height="698" alt="Asset_Checker1" src="https://github.com/user-attachments/assets/9907735a-0820-4d5c-9ef9-60ce1bd8ebbc" />

Clicking this button attempts to download information about the assets associated with the account you are currently logged into. After retrieving the asset names, the script processes each entry to extract publisher information. This step may take some time.

Once the data has been processed, a new button labeled “Check For Chinese Publishers” becomes available:

<img width="738" height="76" alt="Asset_Checker2" src="https://github.com/user-attachments/assets/7a74801a-37b2-4c4c-9159-6fda125867c6" />

Clicking this button checks whether any of your assets list a publisher that matches one of the publishers specified in the CSV file. The results are logged in the console window.

The CSV file included in this repository was created using the PDF file provided in the Unity email regarding assets scheduled for removal.

## Disclaimer

* This script has not been thoroughly tested. Its ability to correctly identify every publisher, particularly those with Chinese characters, cannot be guaranteed.

* The script relies heavily on reflection to access internal Unity members. Since Unity frequently changes its internal APIs, compatibility is not guaranteed. It has been tested with Unity 6.3.10 and will likely fail in earlier versions and potentially in future versions.

* The script performs direct string comparisons. This approach may be unreliable due to inconsistencies introduced when converting the original PDF into a CSV file.

* No real assets were available for testing, so validation was performed using dummy assets.

* Using package IDs instead of publisher name matching would be more reliable and simpler to implement, however the package IDs for the removed assets are not included in the PDF file provided by Unity.
