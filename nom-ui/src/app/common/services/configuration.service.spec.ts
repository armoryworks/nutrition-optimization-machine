import { TestBed } from '@angular/core/testing';
import { ConfigurationService } from './configuration.service';

describe('ConfigurationService', () => {
    let service: ConfigurationService;

    beforeEach(() => {
        TestBed.configureTestingModule({});
        service = TestBed.inject(ConfigurationService);
    });

    it('should be created', () => {
        expect(service).toBeTruthy();
    });

    describe('Mass Units', () => {
        it('should return mass units array', () => {
            const massUnits = service.getMassUnits();
            expect(Array.isArray(massUnits)).toBe(true);
            expect(massUnits.length).toBeGreaterThan(0);
            expect(massUnits).toContain('g');
            expect(massUnits).toContain('kg');
        });

        it('should return volume units array', () => {
            const volumeUnits = service.getVolumeUnits();
            expect(Array.isArray(volumeUnits)).toBe(true);
            expect(volumeUnits.length).toBeGreaterThan(0);
            expect(volumeUnits).toContain('ml');
            expect(volumeUnits).toContain('L');
        });

        it('should return length units array', () => {
            const lengthUnits = service.getLengthUnits();
            expect(Array.isArray(lengthUnits)).toBe(true);
            expect(lengthUnits.length).toBeGreaterThan(0);
            expect(lengthUnits).toContain('cm');
            expect(lengthUnits).toContain('m');
        });

        it('should return time units array', () => {
            const timeUnits = service.getTimeUnits();
            expect(Array.isArray(timeUnits)).toBe(true);
            expect(timeUnits.length).toBeGreaterThan(0);
            expect(timeUnits).toContain('min');
            expect(timeUnits).toContain('hr');
        });

        it('should return file size units array', () => {
            const fileSizeUnits = service.getFileSizeUnits();
            expect(Array.isArray(fileSizeUnits)).toBe(true);
            expect(fileSizeUnits.length).toBeGreaterThan(0);
            expect(fileSizeUnits).toContain('B');
            expect(fileSizeUnits).toContain('KB');
            expect(fileSizeUnits).toContain('MB');
        });

        it('should return shopping units array', () => {
            const shoppingUnits = service.getShoppingUnits();
            expect(Array.isArray(shoppingUnits)).toBe(true);
            expect(shoppingUnits.length).toBeGreaterThan(0);
            expect(shoppingUnits).toContain('piece');
            expect(shoppingUnits).toContain('pack');
        });
    });

    describe('FDA Daily Values', () => {
        it('should return FDA daily values object', () => {
            const fdaValues = service.getFDADailyValue('calories');
            expect(typeof fdaValues).toBe('number');
            expect(fdaValues).toBeGreaterThan(0);
        });

        it('should return undefined for unknown nutrient', () => {
            const fdaValue = service.getFDADailyValue('unknownNutrient');
            expect(fdaValue).toBeUndefined();
        });

        it('should return all FDA daily values', () => {
            const allValues = service.getAllFDADailyValues();
            expect(typeof allValues).toBe('object');
            expect(Object.keys(allValues).length).toBeGreaterThan(0);
        });
    });

    describe('Nutrient Formatting', () => {
        it('should return bold nutrients array', () => {
            const boldNutrients = service.getBoldNutrients();
            expect(Array.isArray(boldNutrients)).toBe(true);
            expect(boldNutrients.length).toBeGreaterThan(0);
        });

        it('should return indented nutrients array', () => {
            const indentedNutrients = service.getIndentedNutrients();
            expect(Array.isArray(indentedNutrients)).toBe(true);
            expect(indentedNutrients.length).toBeGreaterThan(0);
        });
    });

    describe('Color Palettes', () => {
        it('should return primary color palette', () => {
            const primaryColors = service.getPrimaryColorPalette();
            expect(Array.isArray(primaryColors)).toBe(true);
            expect(primaryColors.length).toBeGreaterThan(0);
            expect(primaryColors.every(color => /^#[0-9A-F]{6}$/i.test(color))).toBe(true);
        });

        it('should return secondary color palette', () => {
            const secondaryColors = service.getSecondaryColorPalette();
            expect(Array.isArray(secondaryColors)).toBe(true);
            expect(secondaryColors.length).toBeGreaterThan(0);
            expect(secondaryColors.every(color => /^#[0-9A-F]{6}$/i.test(color))).toBe(true);
        });

        it('should return accent color palette', () => {
            const accentColors = service.getAccentColorPalette();
            expect(Array.isArray(accentColors)).toBe(true);
            expect(accentColors.length).toBeGreaterThan(0);
            expect(accentColors.every(color => /^#[0-9A-F]{6}$/i.test(color))).toBe(true);
        });

        it('should return warning color palette', () => {
            const warningColors = service.getWarningColorPalette();
            expect(Array.isArray(warningColors)).toBe(true);
            expect(warningColors.length).toBeGreaterThan(0);
            expect(warningColors.every(color => /^#[0-9A-F]{6}$/i.test(color))).toBe(true);
        });

        it('should return color palette by name', () => {
            const colors = service.getColorPalette('primary');
            expect(Array.isArray(colors)).toBe(true);
            expect(colors.length).toBeGreaterThan(0);
        });

        it('should return default color palette for unknown name', () => {
            const colors = service.getColorPalette('unknown');
            expect(Array.isArray(colors)).toBe(true);
            expect(colors.length).toBeGreaterThan(0);
        });
    });

    describe('File Validation', () => {
        it('should validate allowed file types', () => {
            expect(service.isFileTypeAllowed('image.jpg', 'image')).toBe(true);
            expect(service.isFileTypeAllowed('document.pdf', 'document')).toBe(true);
            expect(service.isFileTypeAllowed('video.mp4', 'video')).toBe(true);
        });

        it('should reject disallowed file types', () => {
            expect(service.isFileTypeAllowed('script.exe', 'image')).toBe(false);
            expect(service.isFileTypeAllowed('malware.bat', 'document')).toBe(false);
        });

        it('should validate file size limits', () => {
            const smallFile = 1024 * 1024; // 1MB
            const largeFile = 20 * 1024 * 1024; // 20MB

            expect(service.isFileSizeAllowed(smallFile)).toBe(true);
            expect(service.isFileSizeAllowed(largeFile)).toBe(false);
        });

        it('should return file size limit', () => {
            const limit = service.getFileSizeLimit();
            expect(typeof limit).toBe('number');
            expect(limit).toBeGreaterThan(0);
        });
    });

    describe('Utility Functions', () => {
        it('should generate random color from palette', () => {
            const color = service.generateRandomColor();
            expect(/^#[0-9A-F]{6}$/i.test(color)).toBe(true);
        });

        it('should generate consistent color for same seed', () => {
            const seed = 'test-seed';
            const color1 = service.generateColorFromSeed(seed);
            const color2 = service.generateColorFromSeed(seed);
            expect(color1).toBe(color2);
        });

        it('should generate different colors for different seeds', () => {
            const color1 = service.generateColorFromSeed('seed1');
            const color2 = service.generateColorFromSeed('seed2');
            expect(color1).not.toBe(color2);
        });

        it('should calculate contrast color correctly', () => {
            const darkColor = '#000000';
            const lightColor = '#FFFFFF';

            expect(service.getContrastColor(darkColor)).toBe('#FFFFFF');
            expect(service.getContrastColor(lightColor)).toBe('#000000');
        });

        it('should format file size correctly', () => {
            expect(service.formatFileSize(1024)).toContain('1 KB');
            expect(service.formatFileSize(1024 * 1024)).toContain('1 MB');
            expect(service.formatFileSize(1024 * 1024 * 1024)).toContain('1 GB');
        });

        it('should format mass value correctly', () => {
            expect(service.formatMass(1000, 'g')).toBe('1 kg');
            expect(service.formatMass(500, 'g')).toBe('500 g');
            expect(service.formatMass(0.5, 'kg')).toBe('500 g');
        });
    });

    describe('Constants', () => {
        it('should have valid mass units', () => {
            expect(service.massUnits).toBeDefined();
            expect(Array.isArray(service.massUnits)).toBe(true);
        });

        it('should have valid volume units', () => {
            expect(service.volumeUnits).toBeDefined();
            expect(Array.isArray(service.volumeUnits)).toBe(true);
        });

        it('should have valid length units', () => {
            expect(service.lengthUnits).toBeDefined();
            expect(Array.isArray(service.lengthUnits)).toBe(true);
        });

        it('should have valid time units', () => {
            expect(service.timeUnits).toBeDefined();
            expect(Array.isArray(service.timeUnits)).toBe(true);
        });

        it('should have valid file size units', () => {
            expect(service.fileSizeUnits).toBeDefined();
            expect(Array.isArray(service.fileSizeUnits)).toBe(true);
        });

        it('should have valid shopping units', () => {
            expect(service.shoppingUnits).toBeDefined();
            expect(Array.isArray(service.shoppingUnits)).toBe(true);
        });
    });
});
